using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

// DynamoDB and the bank are both down
public class DependencyOutageTests : IClassFixture<OutageFixture>
{
    private readonly OutageFixture fixture;

    public DependencyOutageTests(OutageFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task HealthLive_StaysHealthy()
    {
        using HttpClient api = this.fixture.CreateClient();

        using HttpResponseMessage response = await api.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostPayment_Returns500ProblemWithoutTheExceptionDetails()
    {
        // the PaymentId lookup comes first: without it the gateway can't tell a replay from a new payment
        using HttpClient api = this.fixture.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/api/payments", TestPayments.Create(TestPayments.AuthorizedCard));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("/api/payments", problem.RootElement.GetProperty("instance").GetString());
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    [Fact]
    public async Task GetPayment_Returns500ProblemWithoutTheExceptionDetails()
    {
        using HttpClient api = this.fixture.CreateClient();
        string path = $"/api/payments/{Guid.NewGuid()}";

        using HttpResponseMessage response = await api.GetAsync(path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(path, problem.RootElement.GetProperty("instance").GetString());
        Assert.Equal(TraceResponse.TraceIdOf(response), problem.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }
}
