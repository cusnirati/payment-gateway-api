using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class CardNumberLuhnRuleTests
{
    private readonly Mock<ILogger<CardNumberLuhnRule>> logger = new();
    private readonly CardNumberLuhnRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public CardNumberLuhnRuleTests()
    {
        this.rule = new CardNumberLuhnRule(this.logger.Object);
    }

    [Theory]
    [InlineData("2223000000000007")]
    [InlineData("5123450000000008")]
    [InlineData("4318722273695740")]
    [InlineData("4111111111111111")]
    [InlineData("378282246310005")] // odd length
    [InlineData("79927398713")] // the textbook example, digits doubled to more than 9
    public void Validate_PassesTheLuhnChecksum_ReturnsNull(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("2223000000000008")]
    [InlineData("79927398710")]
    [InlineData("4111111111111121")] // one digit off a valid number
    public void Validate_FailsTheLuhnChecksum_ReturnsCardNumberError(string cardNumber)
    {
        this.payment.CardNumber = cardNumber;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("CardNumber", error.Field);
        Assert.Equal("Card number failed the Luhn checksum.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
