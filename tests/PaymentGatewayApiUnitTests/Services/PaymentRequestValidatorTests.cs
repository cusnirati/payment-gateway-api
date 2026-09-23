using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentRequestValidatorTests
{
    // the clock is pinned to 15 September 2026
    private readonly PaymentRequestValidator validator = new(
        NullLogger<PaymentRequestValidator>.Instance,
        new FakeTimeProvider(TestData.Now),
        Options.Create(new PaymentOptions { SupportedCurrencies = ["GBP", "EUR", "USD"] }));

    private void AssertValid(PaymentRequest request)
    {
        Assert.Empty(this.validator.Validate(request));
    }

    private void AssertOnlyInvalid(PaymentRequest request, string field)
    {
        IReadOnlyDictionary<string, string[]> errors = this.validator.Validate(request);

        KeyValuePair<string, string[]> error = Assert.Single(errors);
        Assert.Equal(field, error.Key);
        Assert.NotEmpty(error.Value);
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        this.AssertValid(TestData.ValidPaymentRequest());
    }

    [Fact]
    public void Validate_EveryFieldInvalid_ReturnsEveryError()
    {
        PaymentRequest request = new()
        {
            CardNumber = "1234",
            ExpiryMonth = 13,
            ExpiryYear = 2027,
            Currency = "XXX",
            Amount = 0,
            Cvv = "1"
        };

        IReadOnlyDictionary<string, string[]> errors = this.validator.Validate(request);

        Assert.Equal(["amount", "cardNumber", "currency", "cvv", "expiryMonth"], [.. new SortedSet<string>(errors.Keys)]);
    }

    [Theory]
    [InlineData("22224053432488")]      // 14 digits
    [InlineData("2222405343248873")]    // 16 digits
    [InlineData("2222405343248873123")] // 19 digits
    public void Validate_CardNumberOf14To19Digits_IsValid(string cardNumber)
    {
        this.AssertValid(TestData.ValidPaymentRequest() with { CardNumber = cardNumber });
    }

    [Theory]
    [InlineData("2222405343248")]        // 13 digits
    [InlineData("22224053432488731234")] // 20 digits
    [InlineData("22224053432488ab")]
    [InlineData("2222 4053 4324 8873")]
    [InlineData("٢٢٢٢٤٠٥٣٤٣٢٤٨٨٧٣")]     // Arabic-Indic digits match \d, not [0-9]
    [InlineData("")]
    public void Validate_CardNumberNotDigits_IsInvalid(string cardNumber)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { CardNumber = cardNumber }, "cardNumber");
    }

    [Theory]
    [InlineData(9, 2026)]  // current month
    [InlineData(10, 2026)]
    [InlineData(1, 2027)]
    [InlineData(12, 2046)] // 20 years ahead
    public void Validate_CurrentOrFutureExpiry_IsValid(int month, int year)
    {
        this.AssertValid(TestData.ValidPaymentRequest() with { ExpiryMonth = month, ExpiryYear = year });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Validate_ExpiryMonthOutOfRange_IsInvalid(int month)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { ExpiryMonth = month }, "expiryMonth");
    }

    [Theory]
    [InlineData(8, 2026)]           // previous month
    [InlineData(12, 2025)]
    [InlineData(1, 2047)]           // more than 20 years ahead
    [InlineData(1, 0)]              // out of DateTime's range
    [InlineData(1, -1)]
    [InlineData(1, int.MaxValue)]
    public void Validate_ExpiredOrTooFarAhead_IsInvalid(int month, int year)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { ExpiryMonth = month, ExpiryYear = year }, "expiryYear");
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("gbp")]
    public void Validate_SupportedCurrency_IsValid(string currency)
    {
        this.AssertValid(TestData.ValidPaymentRequest() with { Currency = currency });
    }

    [Theory]
    [InlineData("JPY")] // a real code, but not a supported one
    [InlineData("123")]
    [InlineData("GBPX")]
    [InlineData("")]
    public void Validate_UnsupportedCurrency_IsInvalid(string currency)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { Currency = currency }, "currency");
    }

    [Theory]
    [InlineData(1)] // one penny
    [InlineData(1050)]
    public void Validate_PositiveMinorUnitAmount_IsValid(int amount)
    {
        this.AssertValid(TestData.ValidPaymentRequest() with { Amount = amount });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_ZeroOrNegativeAmount_IsInvalid(int amount)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { Amount = amount }, "amount");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    public void Validate_CvvOfThreeOrFourDigits_IsValid(string cvv)
    {
        this.AssertValid(TestData.ValidPaymentRequest() with { Cvv = cvv });
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData("")]
    public void Validate_CvvNotThreeOrFourDigits_IsInvalid(string cvv)
    {
        this.AssertOnlyInvalid(TestData.ValidPaymentRequest() with { Cvv = cvv }, "cvv");
    }
}
