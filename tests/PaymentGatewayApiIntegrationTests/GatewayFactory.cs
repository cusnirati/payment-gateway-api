using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PaymentGatewayApi.Services;
using PaymentGatewayApiIntegrationTests;
using Serilog.Core;
using Testcontainers.DynamoDb;
using Xunit;

[assembly: AssemblyFixture(typeof(GatewayFactory))]

namespace PaymentGatewayApiIntegrationTests;

// the whole api in process: real DynamoDB Local in a container, the real bank client and its
// resilience pipeline in front of a stub bank, and every log event collected
public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string MerchantAKey = "merchant-a-key";
    public const string MerchantBKey = "merchant-b-key";

    private readonly DynamoDbContainer dynamoContainer = new DynamoDbBuilder("amazon/dynamodb-local:3.3.1").Build();

    public StubBank Bank { get; } = new();

    public CollectingSink Logs { get; } = new();

    public IAmazonDynamoDB NewDynamoClient()
    {
        return new AmazonDynamoDBClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonDynamoDBConfig { ServiceURL = this.dynamoContainer.GetConnectionString(), MaxErrorRetry = 0 });
    }

    public async ValueTask InitializeAsync()
    {
        await this.dynamoContainer.StartAsync();

        // the same schema the compose init script creates
        using IAmazonDynamoDB dynamo = this.NewDynamoClient();
        await dynamo.CreateTableAsync(Table("Payments", "PaymentId"));
        await dynamo.CreateTableAsync(Table("PaymentIdempotencyKeys", "IdempotencyKey"));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await this.dynamoContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // not Development: no local keys, simulator URL or log files from appsettings.Development.json
        builder.UseEnvironment("Testing");

        builder.UseSetting("Bank:BaseUrl", "http://bank.test/");
        builder.UseSetting("Bank:Resilience:AttemptTimeout:Timeout", "00:00:02");
        builder.UseSetting("Bank:Resilience:TotalRequestTimeout:Timeout", "00:00:04");
        builder.UseSetting("ApiKeys:Merchants:0:MerchantId", "merchant-a");
        builder.UseSetting("ApiKeys:Merchants:0:KeySha256", MerchantApiKeysHash(MerchantAKey));
        builder.UseSetting("ApiKeys:Merchants:1:MerchantId", "merchant-b");
        builder.UseSetting("ApiKeys:Merchants:1:KeySha256", MerchantApiKeysHash(MerchantBKey));
        builder.UseSetting("RateLimiting:PermitLimit", "1000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Debug");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAmazonDynamoDB>(_ => this.NewDynamoClient());
            services.AddSingleton<ILogEventSink>(this.Logs);

            // swaps only the innermost handler: the resilience pipeline in front of it is the production one
            services.AddHttpClient<IBankClient, BankClient>().ConfigurePrimaryHttpMessageHandler(() => this.Bank);
        });
    }

    private static string MerchantApiKeysHash(string apiKey)
    {
        return PaymentGatewayApi.Security.MerchantApiKeys.Hash(apiKey);
    }

    private static CreateTableRequest Table(string name, string hashKey)
    {
        return new CreateTableRequest
        {
            TableName = name,
            AttributeDefinitions = [new AttributeDefinition(hashKey, ScalarAttributeType.S)],
            KeySchema = [new KeySchemaElement(hashKey, KeyType.HASH)],
            BillingMode = BillingMode.PAY_PER_REQUEST
        };
    }
}
