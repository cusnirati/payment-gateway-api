using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankResponseProcessorTests
{
    private readonly BankResponseProcessor processor = new(NullLogger<BankResponseProcessor>.Instance);
    private readonly PaymentRequest payment = TestPayments.Create();

    [Fact]
    public async Task ParseBankResponse_Authorized_ReturnsAuthorizedPaymentWithOnlyTheLastFourCardDigits()
    {
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };

        PaymentDb paymentDb = await this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equivalent(
            new PaymentDb
            {
                PaymentId = this.payment.PaymentId,
                CardNumberLastFour = "0007",
                ExpiryMonth = this.payment.ExpiryMonth,
                ExpiryYear = this.payment.ExpiryYear,
                Amount = this.payment.Amount,
                Currency = this.payment.Currency,
                Status = PaymentStatus.Authorized,
                BankAuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
            },
            paymentDb,
            strict: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ParseBankResponse_AuthorizedWithoutAuthorizationCode_ReturnsDeclined(string? authorizationCode)
    {
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = authorizationCode };

        PaymentDb paymentDb = await this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equal(PaymentStatus.Declined, paymentDb.Status);
    }

    [Fact]
    public async Task ParseBankResponse_NotAuthorized_ReturnsDeclinedWithTheBankReason()
    {
        BankResponse bankResponse = new() { Authorized = false, Reason = "insufficient funds" };

        PaymentDb paymentDb = await this.processor.ParseBankResponse(this.payment, bankResponse);

        Assert.Equivalent(
            new PaymentDb
            {
                PaymentId = this.payment.PaymentId,
                CardNumberLastFour = "0007",
                ExpiryMonth = this.payment.ExpiryMonth,
                ExpiryYear = this.payment.ExpiryYear,
                Amount = this.payment.Amount,
                Currency = this.payment.Currency,
                Status = PaymentStatus.Declined,
                BankReason = "insufficient funds"
            },
            paymentDb,
            strict: true);
    }

    [Fact]
    public async Task ParseBankResponse_NoBankResponse_ReturnsUnavailable()
    {
        PaymentDb paymentDb = await this.processor.ParseBankResponse(this.payment, null);

        Assert.Equivalent(
            new PaymentDb
            {
                PaymentId = this.payment.PaymentId,
                CardNumberLastFour = "0007",
                ExpiryMonth = this.payment.ExpiryMonth,
                ExpiryYear = this.payment.ExpiryYear,
                Amount = this.payment.Amount,
                Currency = this.payment.Currency,
                Status = PaymentStatus.Unavailable
            },
            paymentDb,
            strict: true);
    }
}
