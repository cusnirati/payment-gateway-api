using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Serilog.Events;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

public class SecurityTests
{
    private readonly GatewayFactory factory;

    public SecurityTests(GatewayFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Post_WithoutAValidApiKey_UnauthorizedAndTheBankIsNeverCalled(string? apiKey)
    {
        string card = Cards.Unique('3');
        using HttpClient client = this.factory.CreateMerchantClient(apiKey);

        HttpResponseMessage response = await client.PostPayment(Cards.Payment(card));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(this.factory.Bank.RequestsFor(card));
    }

    [Fact]
    public async Task Get_AnotherMerchantsPayment_NotFound()
    {
        using HttpClient merchantA = this.factory.CreateMerchantClient(GatewayFactory.MerchantAKey);
        using HttpClient merchantB = this.factory.CreateMerchantClient(GatewayFactory.MerchantBKey);
        HttpResponseMessage created = await merchantA.PostPayment(Cards.Payment(Cards.Unique('3')));

        HttpResponseMessage response = await merchantB.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_OverTheMerchantsRateLimit_TooManyRequestsWithRetryAfter()
    {
        await using WebApplicationFactory<Program> limited = this.factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PermitLimit", "2");
            builder.UseSetting("RateLimiting:Window", "00:01:00");
        });
        using HttpClient merchantA = limited.CreateMerchantClient(GatewayFactory.MerchantAKey);
        using HttpClient merchantB = limited.CreateMerchantClient(GatewayFactory.MerchantBKey);

        HttpResponseMessage[] allowed = [await merchantA.PostPayment(Cards.Payment(Cards.Unique('3'))), await merchantA.PostPayment(Cards.Payment(Cards.Unique('3')))];
        HttpResponseMessage limitedResponse = await merchantA.PostPayment(Cards.Payment(Cards.Unique('3')));
        HttpResponseMessage otherMerchant = await merchantB.PostPayment(Cards.Payment(Cards.Unique('3')));

        Assert.All(allowed, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.Created, otherMerchant.StatusCode);
    }

    [Fact]
    public async Task Post_TheFullCardNumberNeverReachesALogLine()
    {
        string card = Cards.Unique('3');
        using HttpClient client = this.factory.CreateMerchantClient();

        // an accepted payment and a rejected one, which exercise the most logging
        await client.PostPayment(Cards.Payment(card));
        await client.PostPayment(Cards.Payment(card, amount: 0));

        LogEvent[] leaking = this.factory.Logs.Events
            .Where(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture).Contains(card, StringComparison.Ordinal)
                || logEvent.Properties.Values.Any(value => value.ToString().Contains(card, StringComparison.Ordinal))
                || (logEvent.Exception?.ToString().Contains(card, StringComparison.Ordinal) ?? false))
            .ToArray();
        Assert.Empty(leaking);
        Assert.Contains(this.factory.Logs.Events, logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture).Contains(card[^4..], StringComparison.Ordinal)
            || logEvent.Properties.ContainsKey("PaymentId"));
    }
}
