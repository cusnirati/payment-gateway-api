using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentRequestValidatorTests
{
    private static readonly DateTime Now = new(2026, 9, 15);

    private readonly PaymentRequestValidator validator;

    public PaymentRequestValidatorTests()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SupportedCurrencies:0"] = "GBP",
                ["SupportedCurrencies:1"] = "EUR",
                ["SupportedCurrencies:2"] = "USD"
            })
            .Build();

        this.validator = new PaymentRequestValidator(NullLogger<PaymentRequestValidator>.Instance, config);
    }

    [Theory]
    [InlineData("22224053432488")]      // 14 digits
    [InlineData("2222405343248873")]    // 16 digits
    [InlineData("2222405343248873123")] // 19 digits
    public void IsCardNumberValid_ValidCardNumber_ReturnsTrue(string cardNumber)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.CardNumber = cardNumber;

        Assert.True(this.validator.IsCardNumberValid(payment));
    }

    [Theory]
    [InlineData("2222405343248")]        // 13 digits
    [InlineData("22224053432488731234")] // 20 digits
    [InlineData("22224053432488ab")]
    [InlineData("2222 4053 4324 8873")]
    [InlineData("")]
    public void IsCardNumberValid_InvalidCardNumber_ReturnsFalse(string cardNumber)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.CardNumber = cardNumber;

        Assert.False(this.validator.IsCardNumberValid(payment));
    }

    [Theory]
    [InlineData(9, 2026)]  // current month
    [InlineData(10, 2026)]
    [InlineData(1, 2027)]
    public void IsExpiryValid_CurrentOrFutureExpiry_ReturnsTrue(int month, int year)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        Assert.True(this.validator.IsExpiryValid(payment, Now));
    }

    [Theory]
    [InlineData(8, 2026)]  // previous month
    [InlineData(12, 2025)]
    [InlineData(0, 2027)]
    [InlineData(13, 2027)]
    [InlineData(11, 0)]     // out of DateTime range
    [InlineData(11, 10000)] // more than four digits
    public void IsExpiryValid_PastExpiryOrInvalidMonth_ReturnsFalse(int month, int year)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        Assert.False(this.validator.IsExpiryValid(payment, Now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void IsAmountValid_PositiveAmount_ReturnsTrue(decimal amount)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Amount = amount;

        Assert.True(this.validator.IsAmountValid(payment));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.99)]
    public void IsAmountValid_AmountBelowOne_ReturnsFalse(decimal amount)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Amount = amount;

        Assert.False(this.validator.IsAmountValid(payment));
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("gbp")]
    public void IsCurrencyCodeValid_SupportedCurrency_ReturnsTrue(string currency)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Currency = currency;

        Assert.True(this.validator.IsCurrencyCodeValid(payment));
    }

    [Theory]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPX")]
    [InlineData("123")]
    [InlineData("JPY")]
    public void IsCurrencyCodeValid_UnsupportedCurrency_ReturnsFalse(string currency)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Currency = currency;

        Assert.False(this.validator.IsCurrencyCodeValid(payment));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    public void IsCvvValid_ThreeOrFourDigits_ReturnsTrue(string cvv)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Cvv = cvv;

        Assert.True(this.validator.IsCvvValid(payment));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData("")]
    public void IsCvvValid_InvalidCvv_ReturnsFalse(string cvv)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Cvv = cvv;

        Assert.False(this.validator.IsCvvValid(payment));
    }
}
