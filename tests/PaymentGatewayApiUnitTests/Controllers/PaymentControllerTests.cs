using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Controllers;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Controllers;

public class PaymentControllerTests
{
    private readonly Mock<IPaymentsRepository> paymentRepo = new();
    private readonly Mock<IValidationOrchestrator> validator = new();
    private readonly Mock<IPaymentProcessor> paymentProcessor = new();
    private readonly PaymentController controller;

    public PaymentControllerTests()
    {
        this.validator.Setup(v => v.ValidatePayment(It.IsAny<PaymentRequest>())).Returns(new List<ValidationError>());
        this.paymentProcessor.Setup(p => p.ExecutePayment(It.IsAny<PaymentRequest>()))
            .ReturnsAsync(new PaymentDb { Status = PaymentStatus.Authorized });

        this.controller = new PaymentController(
            NullLogger<PaymentController>.Instance, this.paymentRepo.Object, this.validator.Object, this.paymentProcessor.Object)
        {
            // MVC's ProblemDetailsFactory comes from the request services, as in the running app
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddMvcCore().Services.BuildServiceProvider() }
            }
        };
    }

    [Fact]
    public async Task GetPayment_StoredPayment_ReturnsItsStatus()
    {
        Guid paymentId = Guid.NewGuid();
        this.paymentRepo.Setup(r => r.Get(paymentId.ToString()))
            .ReturnsAsync(new PaymentDb { PaymentId = paymentId.ToString(), Status = PaymentStatus.Declined });

        ActionResult<MerchantResponse> result = await this.controller.GetPayment(paymentId);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        MerchantResponse response = Assert.IsType<MerchantResponse>(ok.Value);
        Assert.Equal(paymentId.ToString(), response.PaymentId);
        Assert.Equal(PaymentStatus.Declined, response.PaymentStatus);
    }

    [Fact]
    public async Task GetPayment_UnknownPayment_Returns404Problem()
    {
        Guid paymentId = Guid.NewGuid();
        this.paymentRepo.Setup(r => r.Get(paymentId.ToString())).ReturnsAsync((PaymentDb?)null);

        ActionResult<MerchantResponse> result = await this.controller.GetPayment(paymentId);

        ObjectResult notFound = Assert.IsType<ObjectResult>(result.Result);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal($"Payment '{paymentId}' was not found.", problem.Detail);
    }

    [Fact]
    public async Task PostPayment_ValidPayment_ReturnsTheProcessedStatusUnderANewPaymentId()
    {
        PaymentRequest payment = TestPayments.Create();
        payment.PaymentId = "chosen-by-the-merchant";
        this.paymentProcessor.Setup(p => p.ExecutePayment(payment)).ReturnsAsync(new PaymentDb { Status = PaymentStatus.Declined });

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(payment);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        MerchantResponse response = Assert.IsType<MerchantResponse>(ok.Value);
        Assert.True(Guid.TryParse(response.PaymentId, out _));
        Assert.Equal(payment.PaymentId, response.PaymentId);
        Assert.Equal(PaymentStatus.Declined, response.PaymentStatus);
    }

    [Fact]
    public async Task PostPayment_AssignsThePaymentIdAndUppercasesTheCurrencyBeforeValidation()
    {
        PaymentRequest payment = TestPayments.Create();
        payment.Currency = "gbp";
        string? validatedPaymentId = null;
        string? validatedCurrency = null;
        this.validator.Setup(v => v.ValidatePayment(payment))
            .Callback<PaymentRequest>(p =>
            {
                validatedPaymentId = p.PaymentId;
                validatedCurrency = p.Currency;
            })
            .Returns(new List<ValidationError>());

        await this.controller.PostPayment(payment);

        Assert.True(Guid.TryParse(validatedPaymentId, out _));
        Assert.Equal("GBP", validatedCurrency);
        this.paymentProcessor.Verify(p => p.ExecutePayment(It.Is<PaymentRequest>(r => r.Currency == "GBP")), Times.Once);
    }

    [Fact]
    public async Task PostPayment_InvalidPayment_Returns400ProblemWithErrorsPerFieldWithoutProcessing()
    {
        this.validator.Setup(v => v.ValidatePayment(It.IsAny<PaymentRequest>())).Returns(new List<ValidationError>
        {
            new() { Field = "cardNumber", ErrorMessage = "Card number failed the Luhn checksum." },
            new() { Field = "expiry", ErrorMessage = "first expiry error" },
            new() { Field = "expiry", ErrorMessage = "second expiry error" }
        });

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(TestPayments.Create());

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(2, problem.Errors.Count);
        Assert.Equal(new[] { "Card number failed the Luhn checksum." }, problem.Errors["cardNumber"]);
        Assert.Equal(new[] { "first expiry error", "second expiry error" }, problem.Errors["expiry"]);
        this.paymentProcessor.Verify(p => p.ExecutePayment(It.IsAny<PaymentRequest>()), Times.Never);
    }
}
