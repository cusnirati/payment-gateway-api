using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankResponseProcessorTests
{
    private readonly Mock<ILogger<BankResponseProcessor>> logger = new();
    private readonly BankResponseProcessor processor;
    private readonly PaymentRequest payment = TestPayments.Create();

    public BankResponseProcessorTests()
    {
        this.processor = new BankResponseProcessor(this.logger.Object);
    }

    [Fact]
    public void ParseBankResponse_Authorized_ReturnsAuthorizedWithTheAuthorizationCode()
    {
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };

        PaymentDb result = this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", result.BankAuthorizationCode);
        Assert.Null(result.BankReason);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Fact]
    public void ParseBankResponse_Declined_ReturnsDeclinedWithTheBankReason()
    {
        BankResponse bankResponse = new() { Authorized = false, Reason = "insufficient funds" };

        PaymentDb result = this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equal(PaymentStatus.Declined, result.Status);
        Assert.Equal("insufficient funds", result.BankReason);
        Assert.Null(result.BankAuthorizationCode);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Fact]
    public void ParseBankResponse_NoBankResponse_ReturnsUnavailableWithoutLoggingAnError()
    {
        PaymentDb result = this.processor.ParseBankResponse(this.payment, null);

        Assert.Equal(PaymentStatus.Unavailable, result.Status);
        Assert.Null(result.BankAuthorizationCode);
        Assert.Null(result.BankReason);
        // BankHttpClient has already logged the failed call
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseBankResponse_AuthorizedWithoutAuthorizationCode_ReturnsUnavailableAndLogsAnError(string? authorizationCode)
    {
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = authorizationCode };

        PaymentDb result = this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equal(PaymentStatus.Unavailable, result.Status);
        Assert.Null(result.BankAuthorizationCode);
        this.logger.VerifyLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData(true, "0bb07405-6d44-4b50-a14f-7ae0beff13ad")]
    [InlineData(false, null)]
    public void ParseBankResponse_AnyOutcome_CopiesThePaymentButOnlyTheLastFourCardDigits(bool authorized, string? authorizationCode)
    {
        BankResponse bankResponse = new() { Authorized = authorized, AuthorizationCode = authorizationCode };

        PaymentDb result = this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equal(this.payment.PaymentId, result.PaymentId);
        Assert.Equal("0007", result.CardNumberLastFour);
        Assert.Equal(this.payment.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(this.payment.ExpiryYear, result.ExpiryYear);
        Assert.Equal(this.payment.Amount, result.Amount);
        Assert.Equal(this.payment.Currency, result.Currency);
    }

    [Fact]
    public void ParseBankResponse_NoBankResponse_StillCopiesThePayment()
    {
        PaymentDb result = this.processor.ParseBankResponse(this.payment, null);

        Assert.Equal(this.payment.PaymentId, result.PaymentId);
        Assert.Equal("0007", result.CardNumberLastFour);
        Assert.Equal(this.payment.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(this.payment.ExpiryYear, result.ExpiryYear);
        Assert.Equal(this.payment.Amount, result.Amount);
        Assert.Equal(this.payment.Currency, result.Currency);
    }
}
