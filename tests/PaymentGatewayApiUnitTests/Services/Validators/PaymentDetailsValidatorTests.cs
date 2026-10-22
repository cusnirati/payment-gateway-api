using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class PaymentDetailsValidatorTests
{
    private readonly PaymentDetailsValidator validator;
    private readonly PaymentRequest payment = TestPayments.Create();

    public PaymentDetailsValidatorTests()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SupportedCurrencies:0"] = "GBP",
                ["SupportedCurrencies:1"] = "EUR",
                ["SupportedCurrencies:2"] = "USD"
            })
            .Build();
        this.validator = new PaymentDetailsValidator(NullLogger<PaymentDetailsValidator>.Instance, config);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(long.MaxValue)]
    public void ValidateAmount_AtLeastOneMinorUnit_ReturnsNull(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.validator.ValidateAmount(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void ValidateAmount_BelowOneMinorUnit_ReturnsError(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.validator.ValidateAmount(this.payment);

        Assert.NotNull(error);
        Assert.Equal("amount", error.Field);
        Assert.Equal("Amount must be a whole number of minor currency units, at least 1.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("gbp")]
    [InlineData("Usd")]
    public void ValidateCurrencyCode_SupportedCurrencyInAnyCase_ReturnsNull(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.validator.ValidateCurrencyCode(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData(" GBP")]
    public void ValidateCurrencyCode_UnsupportedCurrency_ReturnsErrorListingSupportedCurrencies(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.validator.ValidateCurrencyCode(this.payment);

        Assert.NotNull(error);
        Assert.Equal("currency", error.Field);
        Assert.Equal($"Currency '{currency}' is not supported. Supported currencies: GBP, EUR, USD.", error.ErrorMessage);
    }

    [Fact]
    public void ValidateCurrencyCode_NoSupportedCurrenciesConfigured_RejectsEveryCurrency()
    {
        PaymentDetailsValidator unconfigured = new(NullLogger<PaymentDetailsValidator>.Instance, new ConfigurationBuilder().Build());

        ValidationError? error = unconfigured.ValidateCurrencyCode(this.payment);

        Assert.NotNull(error);
        Assert.Equal("currency", error.Field);
        Assert.Equal("Currency 'GBP' is not supported. Supported currencies: .", error.ErrorMessage);
    }
}
