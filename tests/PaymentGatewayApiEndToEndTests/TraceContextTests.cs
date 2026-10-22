using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

// the W3C trace context is the correlation id: a caller's traceparent is continued, through to the bank
public class TraceContextTests
{
    private readonly ApiFixture fixture;

    public TraceContextTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Request_WithTraceparent_ContinuesThatTrace()
    {
        string traceId = ActivityTraceId.CreateRandom().ToHexString();
        using HttpRequestMessage request = new(HttpMethod.Get, $"api/payment/{Guid.NewGuid()}");
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        using HttpResponseMessage response = await this.fixture.Api.SendAsync(request);

        Assert.Equal(traceId, TraceResponse.TraceIdOf(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(traceId, problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task PostPayment_WithTraceparent_PassesTheTraceOnToTheBank()
    {
        string traceId = ActivityTraceId.CreateRandom().ToHexString();
        using HttpRequestMessage request = new(HttpMethod.Post, "api/payment")
        {
            Content = JsonContent.Create(TestPayments.Create(TestPayments.AuthorizedCard))
        };
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        using HttpResponseMessage response = await this.fixture.Api.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(traceId, TraceResponse.TraceIdOf(response));
        JsonElement payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        BankSimulatorRequest bankRequest = Assert.Single(
            await this.fixture.FindBankRequests("$.paymentId", payment.GetProperty("paymentId").GetString()!));
        Assert.Matches($"^00-{traceId}-[0-9a-f]{{16}}-0[01]$", bankRequest.Headers["traceparent"]);
    }
}
