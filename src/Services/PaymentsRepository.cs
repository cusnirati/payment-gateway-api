using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    Task<PaymentDb?> Get(string paymentId);
    Task Put(PaymentDb payment);
}

// PaymentRaw should be versioned for compatibility
public class PaymentsRepository : IPaymentsRepository
{
    private const string PaymentsTable = "Payments";

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
        // no catch: a failure goes up to ErrorHandler, which owns the error log (correlated to the PaymentId by TraceId)
        var dynamoKey = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = paymentId }
        };

        GetItemResponse response = await this.dynamo.GetItemAsync(PaymentsTable, dynamoKey);

        // only a missing item is "not found"; an item we can't read is corrupt data and must surface as a 500
        if (response.Item == null || response.Item.Count == 0)
        {
            return null;
        }

        if (!response.Item.TryGetValue("PaymentRaw", out AttributeValue? rawAttribute) || rawAttribute.S == null)
        {
            throw new InvalidDataException($"Payment '{paymentId}' has no PaymentRaw string attribute.");
        }

        PaymentDb? payment = JsonSerializer.Deserialize<PaymentDb>(rawAttribute.S);
        if (payment == null || payment.PaymentId != paymentId || payment.Status == PaymentStatus.Unknown)
        {
            throw new InvalidDataException($"Payment '{paymentId}' has an invalid PaymentRaw.");
        }

        return payment;
    }

    public async Task Put(PaymentDb payment)
    {
        try
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
                ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) },
                ["CreatedAt"] = new AttributeValue { S = this.timeProvider.GetUtcNow().UtcDateTime.ToString("o") }
            };

            await this.dynamo.PutItemAsync(PaymentsTable, item);
        }
        catch (Exception exception)
        {
            // the bank has already answered, so throwing back 500 here would hide an authorized payment
            // the merchant still gets the status, but a later GET answers 404; we would have an alert in monitoring for this
            this.logger.LogError(
                exception,
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "PutPayment", "Failed");
        }
    }
}
