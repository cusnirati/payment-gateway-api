using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CardNumberRuleTests
{
    private readonly Mock<ILogger<CardNumberRule>> logger = new();
    private readonly CardNumberRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public CardNumberRuleTests()
    {
        this.rule = new CardNumberRule(this.logger.Object);
    }

    [Theory]
    [InlineData("12345678901234")] // 14 digits
    [InlineData("2223000000000007")]
    [InlineData("1234567890123456789")] // 19 digits
    public void Validate_14To19AsciiDigits_ReturnsNull(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4111")]
    [InlineData("1234567890123")] // 13 digits
    [InlineData("12345678901234567890")] // 20 digits
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData(" 4111111111111111")]
    [InlineData("4111111111111111\n")] // $ would let a trailing newline through
    [InlineData("411111111111111a")]
    [InlineData("٤١١١١١١١١١١١١١١١")] // Arabic-Indic digits, \d would match them
    public void Validate_Not14To19AsciiDigits_ReturnsCardNumberError(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("CardNumber", error.Field);
        Assert.Equal("Card number must be a string of 14 to 19 ASCII digits.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
