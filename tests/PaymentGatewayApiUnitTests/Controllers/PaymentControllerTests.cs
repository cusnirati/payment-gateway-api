using System;
using System.Threading;
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
    private readonly Mock<IPaymentRequestValidator> validator = new();
    private readonly Mock<IPaymentService> paymentService = new();
    private readonly PaymentController controller;

    public PaymentControllerTests()
    {
        this.controller = new PaymentController(
            NullLogger<PaymentController>.Instance, this.validator.Object, this.paymentService.Object);
    }

    private void SetupValidator()
    {
        this.validator.Setup(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsExpiryValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsAmountValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsCurrencyCodeValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsCvvValid(It.IsAny<PaymentRequest>())).Returns(true);
    }

    private void SetupPayment(PaymentStatus status)
    {
        this.paymentService
            .Setup(s => s.ProcessPayment(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.Payment(status));
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
        this.paymentService
            .Setup(s => s.FindPayment(TestData.PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.Payment(PaymentStatus.Authorized));

        ActionResult<PostPaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId, TestContext.Current.CancellationToken);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        PostPaymentResponse response = Assert.IsType<PostPaymentResponse>(ok.Value);
        Assert.Equal(TestData.PaymentId.ToString(), response.PaymentId);
        Assert.Equal(PaymentStatus.Authorized, response.Status);
        Assert.Equal("8873", response.CardNumberLastFour);
    }

    [Fact]
    public async Task GetPayment_Missing_ReturnsNotFound()
    {
        this.paymentService
            .Setup(s => s.FindPayment(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        ActionResult<PostPaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task PostPayment_Authorized_ReturnsOkWithPaymentId()
    {
        this.SetupValidator();
        this.SetupPayment(PaymentStatus.Authorized);

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(TestData.PaymentId.ToString(), ok.Value);
    }

    [Theory]
    [InlineData(nameof(IPaymentRequestValidator.IsCardNumberValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsExpiryValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsAmountValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsCurrencyCodeValid))]
    [InlineData(nameof(IPaymentRequestValidator.IsCvvValid))]
    public async Task PostPayment_ValidationFails_ReturnsRejectedWithoutProcessing(string failingCheck)
    {
        this.SetupValidator();
        switch (failingCheck)
        {
            case nameof(IPaymentRequestValidator.IsCardNumberValid):
                this.validator.Setup(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>())).Returns(false);
                break;
            case nameof(IPaymentRequestValidator.IsExpiryValid):
                this.validator.Setup(v => v.IsExpiryValid(It.IsAny<PaymentRequest>())).Returns(false);
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

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Rejected", badRequest.Value);
        this.paymentService.Verify(
            s => s.ProcessPayment(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Unknown)]
    public async Task PostPayment_NotAuthorized_ReturnsBadRequestWithStatus(PaymentStatus status)
    {
        this.SetupValidator();
        this.SetupPayment(status);

        ActionResult<PaymentStatus> result = await this.controller.PostPayment(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(status.ToString(), badRequest.Value);
    }
}
