using System;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CardDetailsValidatorTests
{
    private readonly Mock<TimeProvider> timeProvider = new();
    private readonly CardDetailsValidator validator;

    public CardDetailsValidatorTests()
    {
        this.timeProvider
            .Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        this.validator = new CardDetailsValidator(NullLogger<CardDetailsValidator>.Instance, this.timeProvider.Object);
    }

    [Theory]
    [InlineData("36227206271667")]
    [InlineData("4111111111111111")]
    [InlineData("4000000000000000006")]
    public void ValidateCardNumber_ValidNumber_ReturnsNull(string cardNumber)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4111111111111")]
    [InlineData("41111111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("411111111111111a")]
    [InlineData(" 4111111111111111")]
    [InlineData("٤١١١١١١١١١١١١١١١")]
    public void ValidateCardNumber_InvalidFormat_ReturnsFormatError(string cardNumber)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(payment);

        Assert.NotNull(error);
        Assert.Equal("cardNumber", error.Field);
        Assert.Equal("Card number must be a string of 14 to 19 ASCII digits.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("36227206271668")]
    [InlineData("4111111111111112")]
    [InlineData("4000000000000000007")]
    public void ValidateCardNumber_LuhnFailure_ReturnsLuhnError(string cardNumber)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(payment);

        Assert.NotNull(error);
        Assert.Equal("cardNumber", error.Field);
        Assert.Equal("Card number failed the Luhn checksum.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(10, 2026)]
    [InlineData(11, 2026)]
    [InlineData(1, 2027)]
    [InlineData(12, 2099)]
    public void ValidateCardExpiry_CurrentOrFutureMonth_ReturnsNull(int month, int year)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        ValidationError? error = this.validator.ValidateCardExpiry(payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(9, 2026, "09/2026")]
    [InlineData(12, 2025, "12/2025")]
    [InlineData(1, 2000, "01/2000")]
    public void ValidateCardExpiry_PastMonth_ReturnsExpiredError(int month, int year, string expiryStr)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        ValidationError? error = this.validator.ValidateCardExpiry(payment);

        Assert.NotNull(error);
        Assert.Equal("expiry", error.Field);
        Assert.Equal($"Card expired. Expiry date '{expiryStr}' is before the current month.", error.ErrorMessage);
    }

    [Fact]
    public void ValidateCardExpiry_LastMonthOfPreviousYear_ReturnsExpiredError()
    {
        this.timeProvider
            .Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = 12;
        payment.ExpiryYear = 2026;

        ValidationError? error = this.validator.ValidateCardExpiry(payment);

        Assert.NotNull(error);
        Assert.Equal("expiry", error.Field);
    }

    [Theory]
    [InlineData(0, 2027, "00/2027")]
    [InlineData(13, 2027, "13/2027")]
    [InlineData(-1, 2027, "-01/2027")]
    [InlineData(11, 0, "11/0")]
    [InlineData(11, 27, "11/27")]
    [InlineData(11, 999, "11/999")]
    public void ValidateCardExpiry_InvalidMonthOrYear_ReturnsFormatError(int month, int year, string expiryStr)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        ValidationError? error = this.validator.ValidateCardExpiry(payment);

        Assert.NotNull(error);
        Assert.Equal("expiry", error.Field);
        Assert.Equal(
            $"Expiry date '{expiryStr}' is not valid. Expiry month must be 1 to 12 and expiry year a four-digit year.",
            error.ErrorMessage);
    }

    [Theory]
    [InlineData("000")]
    [InlineData("123")]
    [InlineData("1234")]
    public void ValidateCardCvv_ValidCvv_ReturnsNull(string cvv)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Cvv = cvv;

        ValidationError? error = this.validator.ValidateCardCvv(payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData(" 123")]
    [InlineData("1 23")]
    [InlineData("١٢٣")]
    public void ValidateCardCvv_InvalidCvv_ReturnsError(string cvv)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.Cvv = cvv;

        ValidationError? error = this.validator.ValidateCardCvv(payment);

        Assert.NotNull(error);
        Assert.Equal("cvv", error.Field);
        Assert.Equal("CVV must be a string of 3 or 4 ASCII digits.", error.ErrorMessage);
    }
}
