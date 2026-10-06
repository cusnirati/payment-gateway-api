using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    Task<PaymentDb?> Get(string paymentId);
    Task AddPending(PaymentRequest payment);
    Task Complete(PaymentDb payment);
}

// PaymentRaw should be versioned for compatibility
public class PaymentsRepository : IPaymentsRepository
{
    private readonly string CompletedPaymentsTable = "CompletedPayments";
    private readonly string PendingPaymentsTable = "PendingPayments";

    private readonly ILogger<PaymentsRepository> logger;
    private readonly IAmazonDynamoDB dynamo;
    private readonly TimeProvider timeProvider;

    public PaymentsRepository(ILogger<PaymentsRepository> logger, IAmazonDynamoDB dynamo, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.dynamo = dynamo;
        this.timeProvider = timeProvider;
    }

    public async Task<PaymentDb?> Get(string paymentId)
    {
        // most probably a payment would be in CompletedPayments table
        try
        {
            var dynamoKey = new Dictionary<string, AttributeValue>
            {
                ["PaymentId"] = new AttributeValue { S = paymentId }
            };
            var request = new GetItemRequest()
            {
                Key = dynamoKey,
                TableName = this.CompletedPaymentsTable
            };

            GetItemResponse response = await this.dynamo.GetItemAsync(request);
            if (response.Item == null || !response.Item.Any())
            {
                request = new() { Key = dynamoKey, TableName = this.PendingPaymentsTable };
                response = await this.dynamo.GetItemAsync(request);
            }

            PaymentDb? payment = null;
            if (response.Item != null && response.Item.Any())
            {
                string raw = response.Item["PaymentRaw"].S;
                payment = JsonSerializer.Deserialize<PaymentDb>(raw);
            }
            return payment;
        }
        catch (Exception exception)
        {
            // throw so ErrorMiddleware.cs answers http 500: returning null would report an unreadable payment as not found
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Failed");
            throw;
        }
    }

    public async Task AddPending(PaymentRequest paymentRequest)
    {
        try
        {
            var paymentDb = new PaymentDb
            {
                PaymentId = paymentRequest.PaymentId,
                CardNumberLastFour = paymentRequest.CardNumber.Substring(paymentRequest.CardNumber.Length - 4),
                ExpiryMonth = paymentRequest.ExpiryMonth,
                ExpiryYear = paymentRequest.ExpiryYear,
                Currency = paymentRequest.Currency,
                Amount = paymentRequest.Amount
            };

            var item = new Dictionary<string, AttributeValue>
            {
                ["PaymentId"] = new AttributeValue { S = paymentRequest.PaymentId },
                ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(paymentDb) },
                ["CreatedAt"] = new AttributeValue { S = this.timeProvider.GetUtcNow().UtcDateTime.ToString("o") }
            };

            await this.dynamo.PutItemAsync(this.PendingPaymentsTable, item);
        }
        catch (Exception exception)
        {
            // throw full exception so ErrorMiddleware answers 500
            // the bank must not be called for a payment we cannot store in the db + an alert would be set
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentRequest.PaymentId, "AddPayment", "Failed");
            throw;
        }
    }

    public async Task Complete(PaymentDb payment)
    {
        try
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
                ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) },
                ["CreatedAt"] = new AttributeValue { S = this.timeProvider.GetUtcNow().UtcDateTime.ToString("o") }
            };

            var addToCompletedTransaction = new TransactWriteItem()
            {
                Put = new Put
                {
                    TableName = this.CompletedPaymentsTable,
                    Item = item
                }
            };
            var deleteFromPendingTransaction = new TransactWriteItem()
            {
                Delete = new Delete
                {
                    TableName = this.PendingPaymentsTable,
                    Key = new Dictionary<string, AttributeValue> { ["PaymentId"] = new AttributeValue { S = payment.PaymentId } }
                }
            };

            // a payment can be only in 1 table
            var request = new TransactWriteItemsRequest()
            {
                TransactItems = new List<TransactWriteItem> { addToCompletedTransaction, deleteFromPendingTransaction }
            };

            await this.dynamo.TransactWriteItemsAsync(request);
        }
        catch (Exception exception)
        {
            // the bank has already answered, so throwing back 500 here would hide an authorized payment
            // the payment stays in PendingPayments, which is what reconciliation picks up; PaymentRaw keeps the bank outcome
            // we would have an alert in monitoring for this scenario, should never happen
            this.logger.LogError(
                exception,
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "CompletePayment", "Failed");
        }
    }
}
