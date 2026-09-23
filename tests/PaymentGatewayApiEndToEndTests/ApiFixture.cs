using System;
using System.Net.Http;
using System.Threading.Tasks;
using PaymentGatewayApiEndToEndTests;
using Xunit;

[assembly: AssemblyFixture(typeof(ApiFixture))]

namespace PaymentGatewayApiEndToEndTests;

// shared by every test, waits for the api started by docker compose to answer its liveness check
public sealed class ApiFixture : IAsyncLifetime
{
    // the local keys configured in docker-compose.yml and appsettings.Development.json
    public const string MerchantAKey = "dev-key-merchant-a";
    public const string MerchantBKey = "dev-key-merchant-b";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri baseAddress = new(Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5067");

    public ApiFixture()
    {
        this.Client = this.CreateClient(MerchantAKey);
        this.Anonymous = this.CreateClient(null);
    }

    // authenticated as merchant A
    public HttpClient Client { get; }

    public HttpClient Anonymous { get; }

    public HttpClient CreateClient(string? apiKey)
    {
        HttpClient client = new() { BaseAddress = this.baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public async ValueTask InitializeAsync()
    {
        DateTime deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage response = await this.Anonymous.GetAsync(new Uri("health/live", UriKind.Relative));
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"Payment gateway not reachable at {this.baseAddress} after {StartupTimeout.TotalSeconds}s. " +
            "Start the stack from src/ with: docker compose --profile api up -d --build");
    }

    public ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        this.Anonymous.Dispose();
        return ValueTask.CompletedTask;
    }
}
