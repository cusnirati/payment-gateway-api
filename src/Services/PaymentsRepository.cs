using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    // stores a new payment, failing if its id already exists
    Task Add(Payment payment, CancellationToken cancellationToken);

    // records the bank's outcome on a payment that is still pending
    Task Complete(Payment payment, CancellationToken cancellationToken);

    Task<Payment?> Find(Guid paymentId, CancellationToken cancellationToken);
}

// one item per payment, keyed by PaymentId, with one attribute per field; the item is the storage
// contract, kept apart from the public response so either can change without migrating the other
public class PaymentsRepository : IPaymentsRepository
{
    private readonly IAmazonDynamoDB dynamo;
    private readonly string tableName;

    public PaymentsRepository(IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options)
    {
        this.dynamo = dynamo;
        this.tableName = options.Value.PaymentsTable;
    }

    public async Task Add(Payment payment, CancellationToken cancellationToken)
    {
        PutItemRequest request = new()
        {
            TableName = this.tableName,
            Item = ToItem(payment),
            ConditionExpression = "attribute_not_exists(PaymentId)"
        };

        await this.dynamo.PutItemAsync(request, cancellationToken);
    }

    public async Task Complete(Payment payment, CancellationToken cancellationToken)
    {
        // an outcome is recorded once: a payment that already left Pending is never overwritten
        PutItemRequest request = new()
        {
            TableName = this.tableName,
            Item = ToItem(payment),
            ConditionExpression = "#status = :pending",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pending"] = new AttributeValue { S = nameof(PaymentStatus.Pending) }
            }
        };

        await this.dynamo.PutItemAsync(request, cancellationToken);
    }

    public async Task<Payment?> Find(Guid paymentId, CancellationToken cancellationToken)
    {
        GetItemRequest request = new()
        {
            TableName = this.tableName,
            Key = new Dictionary<string, AttributeValue> { ["PaymentId"] = new AttributeValue { S = paymentId.ToString() } }
        };

        GetItemResponse response = await this.dynamo.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 } item ? FromItem(item) : null;
    }

    private static Dictionary<string, AttributeValue> ToItem(Payment payment)
    {
        Dictionary<string, AttributeValue> item = new()
        {
            ["PaymentId"] = new AttributeValue { S = payment.Id.ToString() },
            ["MerchantId"] = new AttributeValue { S = payment.MerchantId },
            ["Status"] = new AttributeValue { S = payment.Status.ToString() },
            ["CardNumberLastFour"] = new AttributeValue { S = payment.CardNumberLastFour },
            ["ExpiryMonth"] = new AttributeValue { N = payment.ExpiryMonth.ToString(CultureInfo.InvariantCulture) },
            ["ExpiryYear"] = new AttributeValue { N = payment.ExpiryYear.ToString(CultureInfo.InvariantCulture) },
            ["Currency"] = new AttributeValue { S = payment.Currency },
            ["Amount"] = new AttributeValue { N = payment.Amount.ToString(CultureInfo.InvariantCulture) },
            ["CreatedAt"] = new AttributeValue { S = payment.CreatedAt.ToString("O", CultureInfo.InvariantCulture) },
            ["UpdatedAt"] = new AttributeValue { S = payment.UpdatedAt.ToString("O", CultureInfo.InvariantCulture) }
        };

        if (payment.AuthorizationCode is not null)
        {
            item["AuthorizationCode"] = new AttributeValue { S = payment.AuthorizationCode };
        }

        return item;
    }

    private static Payment FromItem(Dictionary<string, AttributeValue> item)
    {
        return new Payment
        {
            Id = Guid.Parse(item["PaymentId"].S),
            MerchantId = item["MerchantId"].S,
            Status = Enum.Parse<PaymentStatus>(item["Status"].S),
            CardNumberLastFour = item["CardNumberLastFour"].S,
            ExpiryMonth = int.Parse(item["ExpiryMonth"].N, CultureInfo.InvariantCulture),
            ExpiryYear = int.Parse(item["ExpiryYear"].N, CultureInfo.InvariantCulture),
            Currency = item["Currency"].S,
            Amount = int.Parse(item["Amount"].N, CultureInfo.InvariantCulture),
            AuthorizationCode = item.TryGetValue("AuthorizationCode", out AttributeValue? code) ? code.S : null,
            CreatedAt = DateTimeOffset.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(item["UpdatedAt"].S, CultureInfo.InvariantCulture)
        };
    }
}
