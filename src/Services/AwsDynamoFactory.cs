using System;
using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Services;

public interface IAwsDynamoFactory
{
    AmazonDynamoDBClient GetClient();
}

public class AwsDynamoFactory : IAwsDynamoFactory
{
    private readonly ILogger<AwsDynamoFactory> logger;
    private readonly IConfiguration config;

    public AwsDynamoFactory(ILogger<AwsDynamoFactory> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public AmazonDynamoDBClient GetClient()
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