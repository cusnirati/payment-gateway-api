using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models.Responses;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    Task<PostPaymentResponse?> Get(string paymentId);
    Task Add(PostPaymentResponse payment);
    Task Complete(PostPaymentResponse payment);
}

// PaymentRaw should be versioned for compatibility
public class PaymentsRepository : IPaymentsRepository
{
    private readonly ILogger<PaymentsRepository> logger;
    private readonly IAmazonDynamoDB dynamo;
    private readonly TimeProvider timeProvider;

    public PaymentsRepository(ILogger<PaymentsRepository> logger, IAmazonDynamoDB dynamo, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.dynamo = dynamo;
        this.timeProvider = timeProvider;
    }

    public async Task<PostPaymentResponse?> Get(string paymentId)
    {
        var dynamoKey = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = paymentId }
        };

        PostPaymentResponse? payment = null;
        try
        {
            // completed first, it is where almost every payment ends up; fall back to pending for in-flight ones
            GetItemRequest request = new() { Key = dynamoKey, TableName = "CompletedPayments" };

            GetItemResponse response = await this.dynamo.GetItemAsync(request);
            if (response.Item == null || !response.Item.Any())
            {
                request = new() { Key = dynamoKey, TableName = "PendingPayments" };
                response = await this.dynamo.GetItemAsync(request);
            }

            if (response.Item != null && response.Item.Any())
            {
                string raw = response.Item["PaymentRaw"].S;
                payment = JsonSerializer.Deserialize<PostPaymentResponse>(raw);
            }
        }
        catch (Exception exception)
        {
            // throw so ErrorMiddleware.cs answers http 500: returning null would report an unreadable payment as not found
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Failed");
            throw;
        }

        return payment;
    }

    public async Task Add(PostPaymentResponse payment)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) },
            ["CreatedAt"] = new AttributeValue { S = this.timeProvider.GetUtcNow().UtcDateTime.ToString("o") }
        };

        try
        {
            await this.dynamo.PutItemAsync("PendingPayments", item);
        }
        catch (Exception exception)
        {
            // rethrow so ErrorMiddleware answers 500: the bank must not be called for a payment we could not record
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "AddPayment", "Failed");
            throw;
        }
    }

    public async Task Complete(PostPaymentResponse payment)
    {
        Dictionary<string, AttributeValue> item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) },
            ["CreatedAt"] = new AttributeValue { S = this.timeProvider.GetUtcNow().UtcDateTime.ToString("o") }
        };

        // move the payment in one transaction, so it is never in both tables or in neither
        TransactWriteItemsRequest request = new()
        {
            TransactItems = new List<TransactWriteItem>
            {
                new() { Put = new Put { TableName = "CompletedPayments", Item = item } },
                new()
                {
                    Delete = new Delete
                    {
                        TableName = "PendingPayments",
                        Key = new Dictionary<string, AttributeValue> { ["PaymentId"] = new AttributeValue { S = payment.PaymentId } }
                    }
                }
            }
        };

        try
        {
            await this.dynamo.TransactWriteItemsAsync(request);
        }
        catch (Exception exception)
        {
            // the bank has already answered, so throwing back 500 here would hide an authorized payment
            // the payment stays in PendingPayments, which is what reconciliation picks up; PaymentRaw keeps the bank outcome
            // we would have an alert in monitoring for this scenario, should never happen
            this.logger.LogError(
                exception,
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}, PaymentRaw={PaymentRaw}",
                payment.PaymentId, "CompletePayment", "Failed", item["PaymentRaw"].S);
        }
    }
}
