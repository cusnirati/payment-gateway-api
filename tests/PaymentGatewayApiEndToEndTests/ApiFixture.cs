using System;
using System.Net.Http;
using System.Threading.Tasks;
using PaymentGatewayApiEndToEndTests;
using Xunit;

[assembly: AssemblyFixture(typeof(ApiFixture))]

namespace PaymentGatewayApiEndToEndTests;

// shared by every test, waits for the api started by docker compose to answer its healthcheck
public sealed class ApiFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    public HttpClient Client { get; }

    public ApiFixture()
    {
        string baseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5067";

        // long timeout, the api retries the bank's 503 before answering
        this.Client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        this.Client.DefaultRequestHeaders.Add("CallerId", "e2e-tests");
    }

    public async ValueTask InitializeAsync()
    {
        DateTime deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage response = await this.Client.GetAsync("api/payment");
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
            $"Payment gateway not reachable at {this.Client.BaseAddress} after {StartupTimeout.TotalSeconds}s. " +
            "Start the stack from src/ with: docker compose --profile api up -d --build");
    }

    public ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        return ValueTask.CompletedTask;
    }
}
