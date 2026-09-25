using System;
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
        this.paymentRepo.Verify(r => r.Add(It.IsAny<PostPaymentResponse>()), Times.Once);
        this.paymentRepo.Verify(r => r.Update(parsed), Times.Once);
    }

    [Fact]
    public async Task ExecutePayment_PersistsPendingBeforeCallingBank()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        PostPaymentResponse? pending = null;
        PaymentStatus? pendingStatus = null;
        bool bankCalledBeforeAdd = false;
        this.paymentRepo
            .Setup(r => r.Add(It.IsAny<PostPaymentResponse>()))
            .Callback<PostPaymentResponse>(p =>
            {
                pending = p;
                pendingStatus = p.Status;
                bankCalledBeforeAdd = this.bankHttpClient.Invocations.Count > 0;
            })
            .Returns(Task.CompletedTask);
        this.SetupBank(payment, HttpStatusCode.OK, new PostPaymentResponse { PaymentId = payment.PaymentId, Status = PaymentStatus.Authorized });

        await this.service.ExecutePayment(payment);

        Assert.NotNull(pending);
        Assert.False(bankCalledBeforeAdd);
        Assert.Equal(PaymentStatus.Pending, pendingStatus);
        Assert.Equal(TestData.PaymentId, pending.PaymentId);
        Assert.Equal("8873", pending.CardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, pending.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, pending.ExpiryYear);
        Assert.Equal(payment.Currency, pending.Currency);
        Assert.Equal(payment.Amount, pending.Amount);
    }

    [Fact]
    public async Task ExecutePayment_PendingPersistFails_ThrowsWithoutCallingBank()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        this.paymentRepo.Setup(r => r.Add(It.IsAny<PostPaymentResponse>())).ThrowsAsync(new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.service.ExecutePayment(payment));

        this.bankHttpClient.Verify(c => c.PostBankPayment(It.IsAny<PaymentRequest>()), Times.Never);
        this.paymentRepo.Verify(r => r.Update(It.IsAny<PostPaymentResponse>()), Times.Never);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ExecutePayment_BankReturnsNonSuccess_PersistsInternalError(HttpStatusCode statusCode)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        this.SetupBank(payment, statusCode);

        PaymentStatus result = await this.service.ExecutePayment(payment);

        Assert.Equal(PaymentStatus.InternalError, result);
        this.responseProcessor.Verify(
            p => p.ParseBankResponse(It.IsAny<PaymentRequest>(), It.IsAny<HttpResponseMessage>()), Times.Never);
        this.paymentRepo.Verify(
            r => r.Update(It.Is<PostPaymentResponse>(p => p.PaymentId == TestData.PaymentId && p.Status == PaymentStatus.InternalError)),
            Times.Once);
    }

    [Fact]
    public async Task ExecutePayment_BankBodyUnparseable_PersistsInternalErrorWithPaymentDetails()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        PostPaymentResponse parsed = new() { Status = PaymentStatus.InternalError };
        this.SetupBank(payment, HttpStatusCode.OK, parsed);

        PaymentStatus result = await this.service.ExecutePayment(payment);

        Assert.Equal(PaymentStatus.InternalError, result);
        this.paymentRepo.Verify(
            r => r.Update(It.Is<PostPaymentResponse>(p =>
                p.PaymentId == TestData.PaymentId && p.Status == PaymentStatus.InternalError && p.CardNumberLastFour == "8873")),
            Times.Once);
    }
}
