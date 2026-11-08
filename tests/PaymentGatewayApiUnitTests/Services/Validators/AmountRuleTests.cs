using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class AmountRuleTests
{
    private readonly Mock<ILogger<AmountRule>> logger = new();
    private readonly AmountRule rule;
    private readonly PaymentRequest payment = TestPayments.Create();

    public AmountRuleTests()
    {
        this.rule = new AmountRule(this.logger.Object);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(long.MaxValue)]
    public void Validate_AtLeast1_ReturnsNull(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.Null(error);
        this.logger.VerifyNotLogged(LogLevel.Information);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Validate_LessThan1_ReturnsAmountError(long amount)
    {
        this.payment.Amount = amount;

        ValidationError? error = this.rule.Validate(this.payment);

        Assert.NotNull(error);
        Assert.Equal("Amount", error.Field);
        Assert.Equal("Amount must be a whole number of minor currency units, at least 1.", error.ErrorMessage);
        this.logger.VerifyLogged(LogLevel.Information);
    }
}
