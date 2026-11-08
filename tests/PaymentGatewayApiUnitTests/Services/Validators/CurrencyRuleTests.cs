using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CurrencyRuleTests
{
    private readonly Mock<ILogger<CurrencyRule>> logger = new();
    private readonly CurrencyRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public CurrencyRuleTests()
    {
        GatewayOptions options = new() { BankBaseAddress = "http://localhost:8080", SupportedCurrencies = ["GBP", "EUR", "USD"] };
        this.rule = new CurrencyRule(this.logger.Object, Options.Create(options));
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public void Validate_SupportedCurrency_ReturnsNull(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("gbp")] // the controller upper-cases the currency before the rules run
    public void Validate_UnsupportedCurrency_ReturnsCurrencyError(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Currency", error.Field);
        Assert.Equal($"Currency '{currency}' is not supported", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
