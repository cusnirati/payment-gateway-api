using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentProcessorTests
{
    private readonly Mock<IBankHttpClient> bankHttpClient = new();
    private readonly Mock<IBankResponseProcessor> responseProcessor = new();
    private readonly Mock<IPaymentsRepository> paymentRepository = new();
    private readonly PaymentProcessor processor;
    private readonly PaymentRequest payment = TestPayments.Create();

    public PaymentProcessorTests()
    {
        this.processor = new PaymentProcessor(
            NullLogger<PaymentProcessor>.Instance, this.bankHttpClient.Object, this.responseProcessor.Object, this.paymentRepository.Object);
    }

    [Fact]
    public async Task ExecutePayment_BankAnswers_StoresAndReturnsTheParsedPayment()
    {
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };
        PaymentDb parsed = new() { PaymentId = this.payment.PaymentId, Status = PaymentStatus.Authorized };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment)).ReturnsAsync(bankResponse);
        this.responseProcessor.Setup(r => r.ParseBankResponse(this.payment, bankResponse)).Returns(parsed);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(parsed, result);
        this.bankHttpClient.Verify(b => b.PostPayment(this.payment), Times.Once);
        this.paymentRepository.Verify(r => r.Put(parsed), Times.Once);
        this.paymentRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecutePayment_BankUnreachable_StillStoresAndReturnsTheParsedPayment()
    {
        PaymentDb parsed = new() { PaymentId = this.payment.PaymentId, Status = PaymentStatus.Unavailable };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment)).ReturnsAsync((BankResponse?)null);
        this.responseProcessor.Setup(r => r.ParseBankResponse(this.payment, null)).Returns(parsed);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(parsed, result);
        this.paymentRepository.Verify(r => r.Put(parsed), Times.Once);
    }

    [Fact]
    public async Task ExecutePayment_StoresOnlyAfterTheBankAnswered()
    {
        BankResponse bankResponse = new() { Authorized = false, Reason = "insufficient funds" };
        PaymentDb parsed = new() { PaymentId = this.payment.PaymentId, Status = PaymentStatus.Declined };
        MockSequence sequence = new();
        this.bankHttpClient.InSequence(sequence).Setup(b => b.PostPayment(this.payment)).ReturnsAsync(bankResponse);
        this.responseProcessor.InSequence(sequence).Setup(r => r.ParseBankResponse(this.payment, bankResponse)).Returns(parsed);
        this.paymentRepository.InSequence(sequence).Setup(r => r.Put(parsed)).Returns(Task.CompletedTask);

        await this.processor.ExecutePayment(this.payment);

        this.paymentRepository.Verify(r => r.Put(parsed), Times.Once);
    }
}
