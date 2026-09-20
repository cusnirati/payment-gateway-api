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
using Microsoft.Extensions.Configuration;

namespace PaymentGatewayApi.Services;

public interface IPostPaymentRepository
{
    Task Add(PostPaymentResponse payment);
    Task<PostPaymentResponse?> Get(string paymentId);
}

public class Temp
{
    private readonly ILogger<Temp> logger;

    private readonly IConfiguration config;

    public Temp(ILogger<Temp> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public AmazonDynamoDBClient Foo()
    {
        var url = this.config["AwsDynamo"];
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = url,
            Timeout = TimeSpan.FromSeconds(10),
            MaxErrorRetry = 0
        };

        var client = new AmazonDynamoDBClient(config);
        return client;
    }
}

public class PostPaymentRepository : IPostPaymentRepository
{
    private readonly ILogger<PostPaymentRepository> logger;
    private readonly IConfiguration config;

    public PostPaymentRepository(ILogger<PostPaymentRepository> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public async Task Add(PostPaymentResponse payment)
    {
        var url = this.config["AwsDynamo"];
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = url,
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
        // todo log response statusCode
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
            this.logger.LogWarning("GetPayemnt, PaymentId={PaymentId}, Status={Status}", paymentId, "NotFound");

            return null;
        }
    }
}