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
    public void ParseBankResponse_Authorized_ReturnsAnAuthorizedPaymentWithTheAuthorizationCode()
    {
        BankResponse response = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };

        PaymentDb payment = this.processor.ParseBankResponse(this.payment, response);

        PaymentDb expected = new()
        {
            PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d",
            CardNumberLastFour = "0007",
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Amount = 100,
            Currency = "GBP",
            Status = PaymentStatus.Authorized,
            BankAuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
        };
        Assert.Equivalent(expected, payment, strict: true);
        this.logger.VerifyLogged(LogLevel.Information);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Fact]
    public void ParseBankResponse_Declined_ReturnsADeclinedPaymentWithTheBankReason()
    {
        BankResponse response = new() { Authorized = false, Reason = "insufficient funds" };

        PaymentDb payment = this.processor.ParseBankResponse(this.payment, response);

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal("insufficient funds", payment.BankReason);
        Assert.Null(payment.BankAuthorizationCode);
        Assert.Equal("0007", payment.CardNumberLastFour);
        this.logger.VerifyLogged(LogLevel.Information);
    }

    [Fact]
    public void ParseBankResponse_Authorized_LeavesOutAnyReason()
    {
        BankResponse response = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad", Reason = "approved" };

        PaymentDb payment = this.processor.ParseBankResponse(this.payment, response);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Null(payment.BankReason);
    }

    [Fact]
    public void ParseBankResponse_NoBankResponse_ReturnsAnUnavailablePayment()
    {
        PaymentDb payment = this.processor.ParseBankResponse(this.payment, null);

        Assert.Equal(PaymentStatus.Unavailable, payment.Status);
        Assert.Equal("1bfb4527-35b5-4a0c-84d1-c3227fd63b5d", payment.PaymentId);
        Assert.Equal("0007", payment.CardNumberLastFour);
        Assert.Equal(100, payment.Amount);
        Assert.Equal("GBP", payment.Currency);
        Assert.Null(payment.BankAuthorizationCode);
        Assert.Null(payment.BankReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ParseBankResponse_AuthorizedWithoutAuthorizationCode_ReturnsAnUnavailablePaymentAndLogsTheError(string? authorizationCode)
    {
        BankResponse response = new() { Authorized = true, AuthorizationCode = authorizationCode };

        PaymentDb payment = this.processor.ParseBankResponse(this.payment, response);

        Assert.Equal(PaymentStatus.Unavailable, payment.Status);
        Assert.Null(payment.BankAuthorizationCode);
        this.logger.VerifyLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData("12345678901234", "1234")]
    [InlineData("1234567890123456789", "6789")]
    public void ParseBankResponse_KeepsOnlyTheLastFourCardDigits(string cardNumber, string lastFour)
    {
        this.payment.CardNumber = cardNumber;

        PaymentDb payment = this.processor.ParseBankResponse(this.payment, new BankResponse { Authorized = false });

        Assert.Equal(lastFour, payment.CardNumberLastFour);
    }
}
