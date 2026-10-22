using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGatewayApiEndToEndTests;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AssemblyFixture(typeof(ApiFixture))]
// the API hosts share Serilog's static logger and every test shares the bank simulator, one test at a time keeps runs predictable
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace PaymentGatewayApiEndToEndTests;

// the real API in-process, talking to the DynamoDB Local and WireMock bank simulator started by src/docker-compose.yml
public class ApiFixture : IAsyncLifetime
{
    private const string StartDependencies = "Start them first, from src/: docker compose up -d";

    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient bankSimulator;
    private readonly IAmazonDynamoDB dynamo;

    public ApiFixture()
    {
        UseDummyAwsCredentials();
        this.factory = new WebApplicationFactory<Program>();
        this.Api = this.factory.CreateClient();

        // the same addresses the API uses, from its own configuration
        string bankBaseAddress = this.factory.Services.GetRequiredService<IConfiguration>()["SimulatorBankBaseAddress"]!;
        this.bankSimulator = new HttpClient { BaseAddress = new Uri(bankBaseAddress) };
        this.dynamo = this.factory.Services.GetRequiredService<IAmazonDynamoDB>();
    }

    public HttpClient Api { get; }

    // DynamoDB Local accepts any credentials, as in docker-compose.yml, and a real AWS profile never gets near the tests
    public static void UseDummyAwsCredentials()
    {
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "dummy");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "dummy");
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            using HttpResponseMessage mappings = await this.bankSimulator.GetAsync("__admin/mappings");
            mappings.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"The WireMock bank simulator at {this.bankSimulator.BaseAddress} is not running. {StartDependencies}", exception);
        }

        // ready means DynamoDB Local answers and the Payments table is active
        using HttpResponseMessage ready = await this.Api.GetAsync("health/ready");
        if (ready.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"DynamoDB Local is not running or has no Payments table. {StartDependencies}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.Api.Dispose();
        this.bankSimulator.Dispose();
        await this.factory.DisposeAsync();
    }

    // the payment requests the bank simulator received with `value` at `jsonPath` in their body, from WireMock's request journal
    public async Task<List<BankSimulatorRequest>> FindBankRequests(string jsonPath, string value)
    {
        var criteria = new
        {
            method = "POST",
            url = "/payments",
            bodyPatterns = new[] { new { matchesJsonPath = new { expression = jsonPath, equalTo = value } } }
        };
        using HttpResponseMessage response = await this.bankSimulator.PostAsJsonAsync("__admin/requests/find", criteria);
        response.EnsureSuccessStatusCode();

        JsonElement journal = await response.Content.ReadFromJsonAsync<JsonElement>();
        return journal.GetProperty("requests").EnumerateArray().Select(request => new BankSimulatorRequest(request)).ToList();
    }

    // the DynamoDB item the API stored for the payment, null when there is none
    public async Task<Dictionary<string, AttributeValue>?> GetStoredItem(string paymentId)
    {
        GetItemResponse response = await this.dynamo.GetItemAsync(
            "Payments", new Dictionary<string, AttributeValue> { ["PaymentId"] = new AttributeValue { S = paymentId } });
        return response.Item is { Count: > 0 } ? response.Item : null;
    }
}

// one request as logged in WireMock's request journal
public class BankSimulatorRequest
{
    public BankSimulatorRequest(JsonElement loggedRequest)
    {
        using JsonDocument body = JsonDocument.Parse(loggedRequest.GetProperty("body").GetString()!);
        this.Body = body.RootElement.Clone();
        this.Headers = loggedRequest.GetProperty("headers").EnumerateObject()
            .ToDictionary(header => header.Name, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    public JsonElement Body { get; }

    public Dictionary<string, string> Headers { get; }
}
