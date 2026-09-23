using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentsRepository
{
    Task Add(PostPaymentResponse payment, CancellationToken cancellationToken);
    Task<PostPaymentResponse?> Find(string paymentId, CancellationToken cancellationToken);
}

public partial class PaymentsRepository : IPaymentsRepository
{
    private readonly ILogger<PaymentsRepository> logger;
    private readonly IAmazonDynamoDB dynamo;
    private readonly string tableName;

    public PaymentsRepository(ILogger<PaymentsRepository> logger, IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options)
    {
        this.logger = logger;
        this.dynamo = dynamo;
        this.tableName = options.Value.PaymentsTable;
    }

    public async Task Add(PostPaymentResponse payment, CancellationToken cancellationToken)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) }
        };

        try
        {
            await this.dynamo.PutItemAsync(this.tableName, item, cancellationToken);
        }
        catch (Exception exception)
        {
            // the bank has already answered, so failing the request here would hide an authorized payment
            // from the merchant; log the raw item instead so it can be reconciled
            this.LogAddFailed(exception, payment.PaymentId, "Failed", item["PaymentRaw"].S);
        }
    }

    public async Task<PostPaymentResponse?> Find(string paymentId, CancellationToken cancellationToken)
    {
        var key = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = paymentId }
        };

        GetItemRequest request = new() { Key = key, TableName = this.tableName };

        PostPaymentResponse? payment;
        try
        {
            GetItemResponse response = await this.dynamo.GetItemAsync(request, cancellationToken);

            if (response.Item is { Count: > 0 })
            {
                string raw = response.Item["PaymentRaw"].S;
                payment = JsonSerializer.Deserialize<PostPaymentResponse>(raw);
            }
            else
            {
                payment = null;
            }
        }
        catch (Exception exception)
        {
            // rethrow so ErrorMiddleware answers 500: returning null would report an unreadable payment as not found
            this.LogGetFailed(exception, paymentId, "Failed");
            throw;
        }

        if (payment is not null)
        {
            return payment;
        }
        else
        {
            this.LogGetNotFound(paymentId, "NotFound");

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "AddPayment, PaymentId={PaymentId}, Status={Status}, PaymentRaw={PaymentRaw}")]
    private partial void LogAddFailed(Exception exception, string? paymentId, string status, string paymentRaw);

    [LoggerMessage(Level = LogLevel.Error, Message = "GetPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogGetFailed(Exception exception, string paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogGetNotFound(string paymentId, string status);
}
