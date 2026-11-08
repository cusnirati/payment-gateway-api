using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using Xunit;

namespace PaymentGatewayApiUnitTests.Configuration;

public class GatewayOptionsValidatorTests
{
    private readonly GatewayOptionsValidator validator = new();

    private readonly GatewayOptions options = new()
    {
        BankBaseAddress = "http://localhost:8080",
        SupportedCurrencies = ["GBP", "EUR", "USD"]
    };

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://bank.example.com/")]
    [InlineData("http://bank-simulator-wiremock:8080")]
    public void Validate_HttpOrHttpsBankAddressAndCurrencies_Succeeds(string bankBaseAddress)
    {
        this.options.BankBaseAddress = bankBaseAddress;

        ValidateOptionsResult result = this.validator.Validate(null, this.options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/payments")] // an absolute file:// uri on Unix
    [InlineData("localhost:8080")] // parses with "localhost" as the scheme
    [InlineData("ftp://bank.example.com")]
    public void Validate_BankAddressNotAbsoluteHttpOrHttps_Fails(string bankBaseAddress)
    {
        this.options.BankBaseAddress = bankBaseAddress;

        ValidateOptionsResult result = this.validator.Validate(null, this.options);

        Assert.True(result.Failed);
        Assert.Equal($"SimulatorBankBaseAddress must be an absolute http or https url, got '{bankBaseAddress}'", result.FailureMessage);
    }

    [Fact]
    public void Validate_NoSupportedCurrencies_Fails()
    {
        this.options.SupportedCurrencies = [];

        ValidateOptionsResult result = this.validator.Validate(null, this.options);

        Assert.True(result.Failed);
        Assert.Equal("SupportedCurrencies must list at least one currency", result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_BlankSupportedCurrency_Fails(string currency)
    {
        this.options.SupportedCurrencies = ["GBP", currency];

        ValidateOptionsResult result = this.validator.Validate(null, this.options);

        Assert.True(result.Failed);
        Assert.Equal("SupportedCurrencies must not contain blank entries", result.FailureMessage);
    }

    [Theory]
    [InlineData("gbp")]
    [InlineData("Eur")]
    public void Validate_LowerCaseSupportedCurrency_Fails(string currency)
    {
        this.options.SupportedCurrencies = ["USD", currency];

        ValidateOptionsResult result = this.validator.Validate(null, this.options);

        Assert.True(result.Failed);
        Assert.Equal("SupportedCurrencies entries must be upper case ISO 4217 codes", result.FailureMessage);
    }
}
