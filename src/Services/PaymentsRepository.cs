using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    Task<PostPaymentResponse?> Get(string paymentId);
    Task Add(PostPaymentResponse payment);
    Task Update(PostPaymentResponse payment);
}

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ILogger<PaymentsRepository> logger;
    private readonly IAmazonDynamoDB dynamo;

    public PaymentsRepository(ILogger<PaymentsRepository> logger, IAmazonDynamoDB dynamo)
    {
        this.logger = logger;
        this.dynamo = dynamo;
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
            GetItemRequest request = new() { Key = dynamoKey, TableName = "Payments" };

            GetItemResponse response = await this.dynamo.GetItemAsync(request);
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
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) }
        };

        try
        {
            await this.dynamo.PutItemAsync("Payments", item);
        }
        catch (Exception exception)
        {
            // rethrow so ErrorMiddleware answers 500: the bank must not be called for a payment we could not record
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "AddPayment", "Failed");
            throw;
        }
    }

    public async Task Update(PostPaymentResponse payment)
    {
        Dictionary<string, AttributeValue> item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) }
        };

        try
        {
            await this.dynamo.PutItemAsync("Payments", item);
        }
        catch (Exception exception)
        {
            // the bank has already answered, so throwing back 500 here would hide an authorized payment
            // we would have an alert in monitoring for this scenario, should never happen
            this.logger.LogError(
                exception,
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}, PaymentRaw={PaymentRaw}",
                payment.PaymentId, "UpdatePayment", "Failed", item["PaymentRaw"].S);
        }
    }
}