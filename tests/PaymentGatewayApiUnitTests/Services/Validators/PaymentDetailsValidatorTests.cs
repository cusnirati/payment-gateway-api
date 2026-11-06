using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class PaymentDetailsValidatorTests
{
    private readonly PaymentDetailsValidator validator;
    private readonly PaymentRequest payment = TestPayments.Create();

    public PaymentDetailsValidatorTests()
    {
        GatewayOptions options = new() { BankBaseAddress = "http://localhost:8080", SupportedCurrencies = ["GBP", "EUR", "USD"] };
        this.validator = new PaymentDetailsValidator(NullLogger<PaymentDetailsValidator>.Instance, Options.Create(options));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(100L)]
    [InlineData(long.MaxValue)]
    public void ValidateAmount_AtLeastOne_ReturnsNull(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.validator.ValidateAmount(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void ValidateAmount_BelowOne_ReturnsError(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.validator.ValidateAmount(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Amount", error.Field);
        Assert.Equal("Amount must be a whole number of minor currency units, at least 1.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public void ValidateCurrencyCode_SupportedCurrency_ReturnsNull(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.validator.ValidateCurrencyCode(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData(" GBP")]
    [InlineData("gbp")] // the controller upper-cases before validating, the validator itself matches ordinally
    public void ValidateCurrencyCode_UnsupportedCurrency_ReturnsError(string currency)
    {
        this.payment.Currency = currency;

        ValidationError? error = this.validator.ValidateCurrencyCode(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Currency", error.Field);
        Assert.Equal($"Currency '{currency}' is not supported", error.ErrorMessage);
    }
}
