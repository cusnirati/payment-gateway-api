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

    // stores a new payment and its idempotency key atomically; false, storing nothing, when the
    // merchant's key is already held by an earlier payment
    Task<bool> Add(Payment payment, IdempotencyRecord idempotency, CancellationToken cancellationToken);

    Task<IdempotencyRecord?> FindIdempotencyRecord(string merchantId, string key, CancellationToken cancellationToken);

    // records the bank's outcome on a payment that is still pending
    Task Complete(Payment payment, CancellationToken cancellationToken);

    Task<Payment?> Find(Guid paymentId, CancellationToken cancellationToken);
}

// one item per payment, keyed by PaymentId, with one attribute per field; the item is the storage
// contract, kept apart from the public response so either can change without migrating the other.
// Idempotency keys live in their own table, keyed by "{MerchantId}#{Key}" so merchants never collide.
public class PaymentsRepository : IPaymentsRepository
{
    private readonly IAmazonDynamoDB dynamo;
    private readonly TimeProvider timeProvider;
    private readonly string tableName;
    private readonly string idempotencyTableName;

    public PaymentsRepository(IAmazonDynamoDB dynamo, TimeProvider timeProvider, IOptions<DynamoOptions> options)
    {
        this.dynamo = dynamo;
        this.timeProvider = timeProvider;
        this.tableName = options.Value.PaymentsTable;
        this.idempotencyTableName = options.Value.IdempotencyKeysTable;
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

    public async Task<bool> Add(Payment payment, IdempotencyRecord idempotency, CancellationToken cancellationToken)
    {
        TransactWriteItemsRequest request = new()
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = this.tableName,
                        Item = ToItem(payment),
                        ConditionExpression = "attribute_not_exists(PaymentId)"
                    }
                },
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = this.idempotencyTableName,
                        Item = ToItem(idempotency),

                        // an expired key may be taken again even before the TTL has deleted it
                        ConditionExpression = "attribute_not_exists(IdempotencyKey) OR ExpiresAt < :now",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":now"] = new AttributeValue { N = this.timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }
                        }
                    }
                }
            ]
        };

        try
        {
            await this.dynamo.TransactWriteItemsAsync(request, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException exception) when (exception.CancellationReasons is [_, { Code: "ConditionalCheckFailed" }])
        {
            return false;
        }
    }

    public async Task<IdempotencyRecord?> FindIdempotencyRecord(string merchantId, string key, CancellationToken cancellationToken)
    {
        GetItemRequest request = new()
        {
            TableName = this.idempotencyTableName,
            Key = new Dictionary<string, AttributeValue> { ["IdempotencyKey"] = new AttributeValue { S = IdempotencyItemKey(merchantId, key) } },

            // read right after another request's write lost the race for the same key
            ConsistentRead = true
        };

        GetItemResponse response = await this.dynamo.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 } item ? FromIdempotencyItem(item) : null;
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

    private static string IdempotencyItemKey(string merchantId, string key)
    {
        return $"{merchantId}#{key}";
    }

    private static Dictionary<string, AttributeValue> ToItem(IdempotencyRecord record)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["IdempotencyKey"] = new AttributeValue { S = IdempotencyItemKey(record.MerchantId, record.Key) },
            ["MerchantId"] = new AttributeValue { S = record.MerchantId },
            ["Key"] = new AttributeValue { S = record.Key },
            ["PaymentId"] = new AttributeValue { S = record.PaymentId.ToString() },
            ["RequestFingerprint"] = new AttributeValue { S = record.RequestFingerprint },
            ["CreatedAt"] = new AttributeValue { S = record.CreatedAt.ToString("O", CultureInfo.InvariantCulture) },

            // epoch seconds, the format DynamoDB's TTL reads
            ["ExpiresAt"] = new AttributeValue { N = record.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }
        };
    }

    private static IdempotencyRecord FromIdempotencyItem(Dictionary<string, AttributeValue> item)
    {
        return new IdempotencyRecord
        {
            MerchantId = item["MerchantId"].S,
            Key = item["Key"].S,
            PaymentId = Guid.Parse(item["PaymentId"].S),
            RequestFingerprint = item["RequestFingerprint"].S,
            CreatedAt = DateTimeOffset.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["ExpiresAt"].N, CultureInfo.InvariantCulture))
        };
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
