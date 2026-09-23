using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

// a bad setting stops the app at startup, not at the first request that needs it
public class StartupTests
{
    private readonly GatewayFactory factory;

    public StartupTests(GatewayFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("Bank:BaseUrl", "", "BaseUrl")]
    [InlineData("RateLimiting:PermitLimit", "0", "PermitLimit")]
    [InlineData("Payments:IdempotencyKeyRetention", "00:00:00", "IdempotencyKeyRetention")]
    [InlineData("ApiKeys:Merchants:0:KeySha256", "not-a-sha256", "KeySha256")]
    [InlineData("ApiKeys:Merchants:1:MerchantId", "", "MerchantId")]
    public async Task Startup_InvalidSetting_FailsNamingIt(string setting, string value, string member)
    {
        CollectingSink logs = new();
        await using WebApplicationFactory<Program> misconfigured = this.factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(setting, value);
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs));
        });

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());

        Assert.Contains(logs.Events, logEvent =>
            logEvent.Exception is OptionsValidationException exception && exception.Message.Contains(member, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_TwoMerchantsWithOneKey_Fails()
    {
        CollectingSink logs = new();
        await using WebApplicationFactory<Program> misconfigured = this.factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ApiKeys:Merchants:1:KeySha256", PaymentGatewayApi.Security.MerchantApiKeys.Hash(GatewayFactory.MerchantAKey));
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs));
        });

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());

        Assert.Contains(logs.Events, logEvent =>
            logEvent.Exception is OptionsValidationException exception && exception.Message.Contains("unique", StringComparison.Ordinal));
    }
}
