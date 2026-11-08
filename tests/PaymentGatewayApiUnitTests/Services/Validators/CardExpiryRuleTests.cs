using System;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CardExpiryRuleTests
{
    private readonly Mock<ILogger<CardExpiryRule>> logger = new();
    private readonly Mock<TimeProvider> timeProvider = new();
    private readonly CardExpiryRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public CardExpiryRuleTests()
    {
        // the last moment of October 2026, UTC
        this.timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero));
        this.rule = new CardExpiryRule(this.logger.Object, this.timeProvider.Object);
    }

    [Theory]
    [InlineData(10, 2026)] // a card is valid through its expiry month
    [InlineData(11, 2026)]
    [InlineData(1, 2027)]
    [InlineData(12, 9999)]
    public void Validate_CurrentOrFutureMonth_ReturnsNull(int month, int year)
    {
        this.payment.ExpiryMonth = month;
        this.payment.ExpiryYear = year;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Validate_MonthOutside1To12_ReturnsExpiryMonthError(int month)
    {
        this.payment.ExpiryMonth = month;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("ExpiryMonth", error.Field);
        Assert.Equal($"Expiry month '{month}' is not valid. Expiry month must be 1 to 12.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData(27)]
    [InlineData(999)]
    [InlineData(10000)]
    [InlineData(0)]
    public void Validate_YearNotFourDigits_ReturnsExpiryYearError(int year)
    {
        this.payment.ExpiryYear = year;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("ExpiryYear", error.Field);
        Assert.Equal($"Expiry year '{year}' is not valid. Expiry year must be a four-digit year.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }

    [Fact]
    public void Validate_InvalidMonthAndYear_ReturnsOnlyTheMonthError()
    {
        this.payment.ExpiryMonth = 13;
        this.payment.ExpiryYear = 27;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("ExpiryMonth", error.Field);
    }

    [Theory]
    [InlineData(9, 2026, "09/2026")]
    [InlineData(12, 2025, "12/2025")]
    [InlineData(11, 1000, "11/1000")]
    public void Validate_PastMonth_ReturnsCardExpiredError(int month, int year, string expiry)
    {
        this.payment.ExpiryMonth = month;
        this.payment.ExpiryYear = year;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("ExpiryYear", error.Field);
        Assert.Equal($"Card expired. Expiry date '{expiry}' is before the current month.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
