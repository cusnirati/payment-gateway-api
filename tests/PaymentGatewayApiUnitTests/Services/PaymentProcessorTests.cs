using System.Collections.Generic;
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
    public async Task ExecutePayment_SendsToTheBankThenParsesThenStoresTheResult()
    {
        List<string> calls = new();
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };
        PaymentDb authorized = new() { PaymentId = this.payment.PaymentId, Status = PaymentStatus.Authorized };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment))
            .Callback(() => calls.Add("bank"))
            .ReturnsAsync(bankResponse);
        this.responseProcessor.Setup(p => p.ParseBankResponse(this.payment, bankResponse))
            .Callback(() => calls.Add("parse"))
            .ReturnsAsync(authorized);
        this.paymentRepository.Setup(r => r.Put(authorized))
            .Callback(() => calls.Add("store"))
            .Returns(Task.CompletedTask);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(authorized, result);
        Assert.Equal(new[] { "bank", "parse", "store" }, calls);
        this.paymentRepository.Verify(r => r.Put(authorized), Times.Once);
    }

    [Fact]
    public async Task ExecutePayment_BankUnavailable_StillParsesAndStoresThePayment()
    {
        PaymentDb unavailable = new() { PaymentId = this.payment.PaymentId, Status = PaymentStatus.Unavailable };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment)).ReturnsAsync((BankResponse?)null);
        this.responseProcessor.Setup(p => p.ParseBankResponse(this.payment, null)).ReturnsAsync(unavailable);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(unavailable, result);
        this.paymentRepository.Verify(r => r.Put(unavailable), Times.Once);
    }
}
