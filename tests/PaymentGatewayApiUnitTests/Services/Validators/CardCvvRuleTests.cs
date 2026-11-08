using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CardCvvRuleTests
{
    private readonly Mock<ILogger<CardCvvRule>> logger = new();
    private readonly CardCvvRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public CardCvvRuleTests()
    {
        this.rule = new CardCvvRule(this.logger.Object);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("000")]
    [InlineData("1234")]
    public void Validate_3Or4AsciiDigits_ReturnsNull(string cvv)
    {
        this.payment.Cvv = cvv;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData(" 123")]
    [InlineData("123\n")]
    [InlineData("١٢٣")] // Arabic-Indic digits
    public void Validate_Not3Or4AsciiDigits_ReturnsCvvError(string cvv)
    {
        this.payment.Cvv = cvv;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Cvv", error.Field);
        Assert.Equal("CVV must be a string of 3 or 4 ASCII digits.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
