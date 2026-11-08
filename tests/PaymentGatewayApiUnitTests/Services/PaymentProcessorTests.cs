using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentProcessorTests
{
    private readonly Mock<ILogger<PaymentProcessor>> logger = new();
    private readonly Mock<IBankHttpClient> bankHttpClient = new(MockBehavior.Strict);
    private readonly Mock<IBankResponseProcessor> responseProcessor = new(MockBehavior.Strict);
    private readonly Mock<IPaymentsRepository> repository = new(MockBehavior.Strict);
    private readonly PaymentProcessor processor;
    private readonly PaymentRequest payment = TestPayments.Create();
    private readonly PaymentDb parsed = new() { PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d", Status = PaymentStatus.Authorized };

    public PaymentProcessorTests()
    {
        this.processor = new PaymentProcessor(this.logger.Object, this.bankHttpClient.Object, this.responseProcessor.Object, this.repository.Object);
    }

    [Fact]
    public async Task ExecutePayment_PostsToTheBankThenStoresAndReturnsTheParsedPayment()
    {
        List<string> calls = new();
        BankResponse bankResponse = new() { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment)).Callback(() => calls.Add("bank")).ReturnsAsync(bankResponse);
        this.responseProcessor.Setup(p => p.ParseBankResponse(this.payment, bankResponse)).Callback(() => calls.Add("parse")).Returns(this.parsed);
        this.repository.Setup(r => r.Put(this.parsed)).Callback(() => calls.Add("store")).Returns(Task.CompletedTask);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(this.parsed, result);
        Assert.Equal(new[] { "bank", "parse", "store" }, calls);
    }

    [Fact]
    public async Task ExecutePayment_NoBankResponse_StillParsesAndStoresThePayment()
    {
        PaymentDb unavailable = new() { PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d", Status = PaymentStatus.Unavailable };
        this.bankHttpClient.Setup(b => b.PostPayment(this.payment)).ReturnsAsync((BankResponse?)null);
        this.responseProcessor.Setup(p => p.ParseBankResponse(this.payment, null)).Returns(unavailable);
        this.repository.Setup(r => r.Put(unavailable)).Returns(Task.CompletedTask);

        PaymentDb result = await this.processor.ExecutePayment(this.payment);

        Assert.Same(unavailable, result);
        this.repository.Verify(r => r.Put(unavailable), Times.Once);
    }
}
