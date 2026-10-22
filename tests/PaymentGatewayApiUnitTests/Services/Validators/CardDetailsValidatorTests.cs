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
    private readonly PaymentRequest payment = TestPayments.Create();

    public CardDetailsValidatorTests()
    {
        this.timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero));
        this.validator = new CardDetailsValidator(NullLogger<CardDetailsValidator>.Instance, this.timeProvider.Object);
    }

    [Theory]
    [InlineData("30569309025904")]
    [InlineData("378282246310005")]
    [InlineData("4111111111111111")]
    [InlineData("60110000000000001")]
    [InlineData("411111111111111118")]
    [InlineData("6011000000000000001")]
    public void ValidateCardNumber_LuhnValid14To19Digits_ReturnsNull(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("4222222222222")] // 13 digits, Luhn valid
    [InlineData("41111111111111111115")] // 20 digits, Luhn valid
    [InlineData("")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("411111111111111a")]
    [InlineData("4111111111111111\n")] // \z rejects a trailing newline, $ would accept it
    [InlineData("٤١١١١١١١١١١١١١١١")] // Arabic-Indic digits, \d would accept them
    public void ValidateCardNumber_NotAsciiDigitsOrWrongLength_ReturnsFormatError(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(this.payment);

        Assert.NotNull(error);
        Assert.Equal("cardNumber", error.Field);
        Assert.Equal("Card number must be a string of 14 to 19 ASCII digits.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("2223000000000008")]
    public void ValidateCardNumber_LuhnChecksumFails_ReturnsLuhnError(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.validator.ValidateCardNumber(this.payment);

        Assert.NotNull(error);
        Assert.Equal("cardNumber", error.Field);
        Assert.Equal("Card number failed the Luhn checksum.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(6, 2026)] // current month
    [InlineData(7, 2026)]
    [InlineData(1, 2027)]
    [InlineData(12, 2099)]
    public void ValidateCardExpiry_CurrentOrFutureMonth_ReturnsNull(int expiryMonth, int expiryYear)
    {
        this.payment.ExpiryMonth = expiryMonth;
        this.payment.ExpiryYear = expiryYear;

        ValidationError? error = this.validator.ValidateCardExpiry(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(5, 2026, "05/2026")]
    [InlineData(6, 2025, "06/2025")]
    [InlineData(12, 2025, "12/2025")]
    public void ValidateCardExpiry_BeforeCurrentMonth_ReturnsExpiredError(int expiryMonth, int expiryYear, string expiry)
    {
        this.payment.ExpiryMonth = expiryMonth;
        this.payment.ExpiryYear = expiryYear;

        ValidationError? error = this.validator.ValidateCardExpiry(this.payment);

        Assert.NotNull(error);
        Assert.Equal("expiry", error.Field);
        Assert.Equal($"Card expired. Expiry date '{expiry}' is before the current month.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(0, 2027, "00/2027")]
    [InlineData(13, 2027, "13/2027")]
    [InlineData(-1, 2027, "-01/2027")]
    [InlineData(4, 27, "04/27")]
    [InlineData(4, 10000, "04/10000")]
    public void ValidateCardExpiry_InvalidMonthOrYear_ReturnsFormatError(int expiryMonth, int expiryYear, string expiry)
    {
        this.payment.ExpiryMonth = expiryMonth;
        this.payment.ExpiryYear = expiryYear;

        ValidationError? error = this.validator.ValidateCardExpiry(this.payment);

        Assert.NotNull(error);
        Assert.Equal("expiry", error.Field);
        Assert.Equal(
            $"Expiry date '{expiry}' is not valid. Expiry month must be 1 to 12 and expiry year a four-digit year.",
            error.ErrorMessage);
    }

    [Fact]
    public void ValidateCardExpiry_LastSecondOfTheExpiryMonth_ReturnsNull()
    {
        this.timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));
        this.payment.ExpiryMonth = 12;
        this.payment.ExpiryYear = 2026;

        ValidationError? error = this.validator.ValidateCardExpiry(this.payment);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateCardExpiry_FirstSecondOfTheNextYear_ReturnsExpiredError()
    {
        this.timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        this.payment.ExpiryMonth = 12;
        this.payment.ExpiryYear = 2026;

        ValidationError? error = this.validator.ValidateCardExpiry(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Card expired. Expiry date '12/2026' is before the current month.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("000")]
    public void ValidateCardCvv_ThreeOrFourAsciiDigits_ReturnsNull(string cvv)
    {
        this.payment.Cvv = cvv;

        ValidationError? error = this.validator.ValidateCardCvv(this.payment);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData(" 123")]
    [InlineData("123\n")]
    [InlineData("١٢٣")]
    public void ValidateCardCvv_NotThreeOrFourAsciiDigits_ReturnsError(string cvv)
    {
        this.payment.Cvv = cvv;

        ValidationError? error = this.validator.ValidateCardCvv(this.payment);

        Assert.NotNull(error);
        Assert.Equal("cvv", error.Field);
        Assert.Equal("CVV must be a string of 3 or 4 ASCII digits.", error.ErrorMessage);
    }
}
