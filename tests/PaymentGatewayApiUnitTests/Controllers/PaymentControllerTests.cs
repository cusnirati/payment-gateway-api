using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Controllers;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Controllers;

public class PaymentControllerTests
{
    private readonly Mock<IPaymentsRepository> paymentRepo = new();
    private readonly Mock<IPaymentRequestValidator> validator = new();
    private readonly Mock<IBankService> bankService = new();
    private readonly PaymentController controller;

    public PaymentControllerTests()
    {
        this.controller = new PaymentController(
            NullLogger<PaymentController>.Instance,
            this.paymentRepo.Object,
            this.validator.Object,
            this.bankService.Object);
    }

    private void SetupValidator(bool isValid = true)
    {
        this.validator.Setup(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>())).Returns(isValid);
        this.validator.Setup(v => v.IsExpiryValid(It.IsAny<PaymentRequest>(), It.IsAny<DateTime>())).Returns(isValid);
        this.validator.Setup(v => v.IsAmountValid(It.IsAny<PaymentRequest>())).Returns(isValid);
        this.validator.Setup(v => v.IsCurrencyCodeValid(It.IsAny<PaymentRequest>())).Returns(isValid);
        this.validator.Setup(v => v.IsCvvValid(It.IsAny<PaymentRequest>())).Returns(isValid);
    }

    [Fact]
    public void GetHealthcheck_ReturnsOk()
    {
        ActionResult<string> result = this.controller.GetHealthcheck();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("ok", ok.Value);
    }

    [Fact]
    public async Task GetPayment_Found_ReturnsOkWithPayment()
    {
        PostPaymentResponse payment = new() { PaymentId = TestData.PaymentId, Status = PaymentStatus.Authorized };
        this.paymentRepo.Setup(r => r.Get(TestData.PaymentId)).ReturnsAsync(payment);

        ActionResult<PostPaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(payment, ok.Value);
    }

    [Fact]
    public async Task GetPayment_Missing_ReturnsNotFound()
    {
        this.paymentRepo.Setup(r => r.Get(It.IsAny<string>())).ReturnsAsync((PostPaymentResponse?)null);

        ActionResult<PostPaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task PostPayment_Authorized_ReturnsOkWithNewPaymentId()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        this.SetupValidator();
        this.bankService.Setup(b => b.ExecutePayment(payment)).ReturnsAsync(PaymentStatus.Authorized);

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(payment);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        string paymentId = Assert.IsType<string>(ok.Value);
        Assert.True(Guid.TryParse(paymentId, out _));
        Assert.Equal(payment.PaymentId, paymentId);
    }

    [Fact]
    public async Task PostPayment_ClientSuppliedPaymentId_IsReplaced()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        this.SetupValidator();
        this.bankService.Setup(b => b.ExecutePayment(payment)).ReturnsAsync(PaymentStatus.Authorized);

        await this.controller.PostPayment(payment);

        Assert.NotEqual(TestData.PaymentId, payment.PaymentId);
        this.bankService.Verify(b => b.ExecutePayment(It.Is<PaymentRequest>(p => p.PaymentId != TestData.PaymentId)), Times.Once);
    }

    [Theory]
    [InlineData(nameof(IPaymentRequestValidator.IsCardNumberValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsExpiryValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsAmountValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsCurrencyCodeValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsCvvValid))]
    public async Task PostPayment_ValidationFails_ReturnsRejectedWithoutCallingBank(string failingCheck)
    {
        this.SetupValidator();
        switch (failingCheck)
        {
            case nameof(IPaymentRequestValidator.IsCardNumberValid):
                this.validator.Setup(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>())).Returns(false);
                break;
            case nameof(IPaymentRequestValidator.IsExpiryValid):
                this.validator.Setup(v => v.IsExpiryValid(It.IsAny<PaymentRequest>(), It.IsAny<DateTime>())).Returns(false);
                break;
            case nameof(IPaymentRequestValidator.IsAmountValid):
                this.validator.Setup(v => v.IsAmountValid(It.IsAny<PaymentRequest>())).Returns(false);
                break;
            case nameof(IPaymentRequestValidator.IsCurrencyCodeValid):
                this.validator.Setup(v => v.IsCurrencyCodeValid(It.IsAny<PaymentRequest>())).Returns(false);
                break;
            case nameof(IPaymentRequestValidator.IsCvvValid):
                this.validator.Setup(v => v.IsCvvValid(It.IsAny<PaymentRequest>())).Returns(false);
                break;
        }

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(TestData.ValidPaymentRequest());

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(nameof(PaymentStatus.Rejected), badRequest.Value);
        this.bankService.Verify(b => b.ExecutePayment(It.IsAny<PaymentRequest>()), Times.Never);
    }

    [Fact]
    public async Task PostPayment_CardNumberInvalid_SkipsRemainingChecks()
    {
        this.SetupValidator(isValid: false);

        await this.controller.PostPayment(TestData.ValidPaymentRequest());

        this.validator.Verify(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>()), Times.Once);
        this.validator.Verify(v => v.IsExpiryValid(It.IsAny<PaymentRequest>(), It.IsAny<DateTime>()), Times.Never);
        this.validator.Verify(v => v.IsAmountValid(It.IsAny<PaymentRequest>()), Times.Never);
        this.validator.Verify(v => v.IsCurrencyCodeValid(It.IsAny<PaymentRequest>()), Times.Never);
        this.validator.Verify(v => v.IsCvvValid(It.IsAny<PaymentRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.InternalError)]
    public async Task PostPayment_NotAuthorized_ReturnsBadRequestWithStatus(PaymentStatus status)
    {
        this.SetupValidator();
        this.bankService.Setup(b => b.ExecutePayment(It.IsAny<PaymentRequest>())).ReturnsAsync(status);

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(TestData.ValidPaymentRequest());

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(status.ToString(), badRequest.Value);
    }
}
