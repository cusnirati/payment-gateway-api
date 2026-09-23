using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankServiceTests
{
    private readonly Mock<IBankHttpClient> bankHttpClient = new();
    private readonly Mock<IBankResponseProcessor> responseProcessor = new();
    private readonly Mock<IPaymentsRepository> paymentRepo = new();
    private readonly BankService service;

    public BankServiceTests()
    {
        this.service = new BankService(
            NullLogger<BankService>.Instance,
            this.bankHttpClient.Object,
            this.responseProcessor.Object,
            this.paymentRepo.Object);
    }

    private void SetupBank(PaymentRequest payment, HttpStatusCode statusCode, PostPaymentResponse? parsed = null)
    {
        HttpResponseMessage bankResponse = new(statusCode);
        this.bankHttpClient.Setup(c => c.PostBankPayment(payment)).ReturnsAsync(bankResponse);

        if (parsed is not null)
        {
            this.responseProcessor.Setup(p => p.ParseBankResponse(payment, bankResponse)).ReturnsAsync(parsed);
        }
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    public async Task ExecutePayment_BankAnswers_PersistsAndReturnsParsedStatus(PaymentStatus status)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        PostPaymentResponse parsed = new() { PaymentId = payment.PaymentId, Status = status };
        this.SetupBank(payment, HttpStatusCode.OK, parsed);

        PaymentStatus result = await this.service.ExecutePayment(payment);

        Assert.Equal(status, result);
        this.paymentRepo.Verify(r => r.Add(parsed), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ExecutePayment_BankReturnsNonSuccess_ReturnsInternalErrorWithoutPersisting(HttpStatusCode statusCode)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        this.SetupBank(payment, statusCode);

        PaymentStatus result = await this.service.ExecutePayment(payment);

        Assert.Equal(PaymentStatus.InternalError, result);
        this.responseProcessor.Verify(
            p => p.ParseBankResponse(It.IsAny<PaymentRequest>(), It.IsAny<HttpResponseMessage>()), Times.Never);
        this.paymentRepo.Verify(r => r.Add(It.IsAny<PostPaymentResponse>()), Times.Never);
    }

    [Fact]
    public async Task ExecutePayment_BankBodyUnparseable_StillPersistsResponseWithoutPaymentId()
    {
        // NOTE: defect - when the bank body cannot be parsed the processor returns a response with only
        // Status set, and BankService persists it anyway, with a null PaymentId as the DynamoDB hash key
        PaymentRequest payment = TestData.ValidPaymentRequest();
        PostPaymentResponse parsed = new() { Status = PaymentStatus.InternalError };
        this.SetupBank(payment, HttpStatusCode.OK, parsed);

        PaymentStatus result = await this.service.ExecutePayment(payment);

        Assert.Equal(PaymentStatus.InternalError, result);
        this.paymentRepo.Verify(r => r.Add(It.Is<PostPaymentResponse>(p => p.PaymentId == null)), Times.Once);
    }
}
