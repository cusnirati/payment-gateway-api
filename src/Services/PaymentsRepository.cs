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

    public PostPaymentRepository(ILogger<PostPaymentRepository> logger)
    {
        this.logger = logger;
    }

    public async Task Add(PostPaymentResponse payment)
    {
        // todo no hardcode
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = "http://localhost:8000",
            Timeout = TimeSpan.FromSeconds(10),
            MaxErrorRetry = 0
        };
        var client = new AmazonDynamoDBClient(config);

        var item = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = payment.PaymentId },
            ["PaymentRaw"] = new AttributeValue { S = JsonSerializer.Serialize(payment) }
        };


        PutItemResponse response = await client.PutItemAsync("Payments", item);
    }

    public async Task<PostPaymentResponse?> Get(string paymentId)
    {
        // todo no hardcode
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = "http://localhost:8000",
            Timeout = TimeSpan.FromSeconds(10),
            MaxErrorRetry = 0
        };
        var client = new AmazonDynamoDBClient(config);

        var key = new Dictionary<string, AttributeValue>
        {
            ["PaymentId"] = new AttributeValue { S = paymentId }
        };
        GetItemRequest request = new() { Key = key, TableName = "Payments" };
        GetItemResponse response = await client.GetItemAsync(request);

        PostPaymentResponse? payment;
        if (response.Item != null && response.Item.Any())
        {
            string raw = response.Item["PaymentRaw"].S;
            payment = JsonSerializer.Deserialize<PostPaymentResponse>(raw);

            return payment;
        }
        else
        {
            this.logger.LogWarning("Get.Payments.Failed, PaymentId={PaymentId}, Status={Status}", paymentId, "NotFound");

            return null;
        }
    }
}