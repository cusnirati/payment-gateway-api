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

public interface IPostPaymentRepository
{
    Task Add(PostPaymentResponse payment);
    Task<PostPaymentResponse?> Get(string paymentId);
}

public class PostPaymentRepository : IPostPaymentRepository
{
    private readonly ILogger<PostPaymentRepository> logger;
    private readonly IAmazonDynamoDB dynamo;

    public PostPaymentRepository(ILogger<PostPaymentRepository> logger, IAmazonDynamoDB dynamo)
    {
        this.logger = logger;
        this.dynamo = dynamo;
    }

    public async Task Add(PostPaymentResponse payment)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) }
        };
        PutItemResponse response = await this.dynamo.PutItemAsync("Payments", item);
        // todo log response
    }

    public async Task<PostPaymentResponse?> Get(string paymentId)
    {
        var key = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = paymentId }
        };

        GetItemRequest request = new() { Key = key, TableName = "Payments" };
        GetItemResponse response = await this.dynamo.GetItemAsync(request);

        PostPaymentResponse? payment;
        if (response.Item != null && response.Item.Any())
        {
            string raw = response.Item["PaymentRaw"].S;
            payment = JsonSerializer.Deserialize<PostPaymentResponse>(raw);

            return payment;
        }
        else
        {
            this.logger.LogInformation("GetPayemnt, PaymentId={PaymentId}, Status={Status}", paymentId, "NotFound");

            return null;
        }
    }
}