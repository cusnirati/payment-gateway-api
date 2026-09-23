using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Controllers;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Security;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Controllers;

public class PaymentsControllerTests
{
    private readonly Mock<IPaymentRequestValidator> validator = new();
    private readonly Mock<IPaymentService> paymentService = new();
    private readonly PaymentsController controller;

    public PaymentsControllerTests()
    {
        this.validator.Setup(v => v.Validate(It.IsAny<PaymentRequest>())).Returns(new Dictionary<string, string[]>());

        this.controller = new PaymentsController(
            NullLogger<PaymentsController>.Instance, this.validator.Object, this.paymentService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MerchantClaims.MerchantId, TestData.MerchantId)], "test"))
                }
            },
            ProblemDetailsFactory = new SimpleProblemDetailsFactory()
        };
    }

    private void SetupAttempt(PaymentAttempt attempt)
    {
        this.paymentService
            .Setup(s => s.ProcessPayment(TestData.MerchantId, It.IsAny<PaymentRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
    }

    private void SetupPayment(PaymentStatus status)
    {
        this.SetupAttempt(new PaymentAttempt.Processed(TestData.Payment(status)));
    }

    private Task<ActionResult<PaymentResponse>> Post(string? idempotencyKey = null)
    {
        return this.controller.PostPayment(TestData.ValidPaymentRequest(), idempotencyKey, TestContext.Current.CancellationToken);
    }

    private void VerifyNeverProcessed()
    {
        this.paymentService.Verify(
            s => s.ProcessPayment(It.IsAny<string>(), It.IsAny<PaymentRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetPayment_Found_ReturnsPayment()
    {
        this.paymentService
            .Setup(s => s.FindPayment(TestData.MerchantId, TestData.PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.Payment(PaymentStatus.Authorized));

        ActionResult<PaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId, TestContext.Current.CancellationToken);

        PaymentResponse response = Assert.IsType<PaymentResponse>(result.Value);
        Assert.Equal(TestData.PaymentId, response.Id);
        Assert.Equal(PaymentStatus.Authorized, response.Status);
        Assert.Equal("8873", response.CardNumberLastFour);
    }

    [Fact]
    public async Task GetPayment_Missing_ReturnsNotFound()
    {
        this.paymentService
            .Setup(s => s.FindPayment(TestData.MerchantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        ActionResult<PaymentResponse> result = await this.controller.GetPayment(TestData.PaymentId, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)] // the issuer said no, the request itself was fine
    public async Task PostPayment_BankAnswered_ReturnsCreatedPaymentWithLocation(PaymentStatus status)
    {
        this.SetupPayment(status);

        ActionResult<PaymentResponse> result = await this.Post();

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(PaymentsController.GetPayment), created.ActionName);
        Assert.Equal(TestData.PaymentId, created.RouteValues?["paymentId"]);
        PaymentResponse response = Assert.IsType<PaymentResponse>(created.Value);
        Assert.Equal(TestData.PaymentId, response.Id);
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public async Task PostPayment_Invalid_ReturnsValidationProblemWithoutProcessing()
    {
        this.validator
            .Setup(v => v.Validate(It.IsAny<PaymentRequest>()))
            .Returns(new Dictionary<string, string[]> { ["cvv"] = ["Must be 3 or 4 digits."] });

        ActionResult<PaymentResponse> result = await this.Post();

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(["Must be 3 or 4 digits."], problem.Errors["cvv"]);
        this.VerifyNeverProcessed();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("naïve")]
    public async Task PostPayment_MalformedIdempotencyKey_ReturnsValidationProblemWithoutProcessing(string idempotencyKey)
    {
        ActionResult<PaymentResponse> result = await this.Post(idempotencyKey);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.True(problem.Errors.ContainsKey("Idempotency-Key"));
        this.VerifyNeverProcessed();
    }

    [Fact]
    public async Task PostPayment_Replayed_ReturnsTheOriginalOutcomeMarkedAsReplayed()
    {
        this.SetupAttempt(new PaymentAttempt.Replayed(TestData.Payment(PaymentStatus.Authorized)));

        ActionResult<PaymentResponse> result = await this.Post("order-42");

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(TestData.PaymentId, Assert.IsType<PaymentResponse>(created.Value).Id);
        Assert.Equal("true", this.controller.Response.Headers["Idempotent-Replayed"].ToString());
    }

    [Fact]
    public async Task PostPayment_KeyStillInProgress_ReturnsConflictWithPaymentId()
    {
        this.SetupAttempt(new PaymentAttempt.InProgress(TestData.PaymentId));

        ActionResult<PaymentResponse> result = await this.Post("order-42");

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.Equal(TestData.PaymentId, Assert.IsType<ProblemDetails>(objectResult.Value).Extensions["paymentId"]);
    }

    [Fact]
    public async Task PostPayment_KeyReused_ReturnsUnprocessableEntity()
    {
        this.SetupAttempt(new PaymentAttempt.KeyReused());

        ActionResult<PaymentResponse> result = await this.Post("order-42");

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Unknown)]
    public async Task PostPayment_BankCouldNotComplete_ReturnsBadGatewayWithPaymentId(PaymentStatus status)
    {
        this.SetupPayment(status);

        ActionResult<PaymentResponse> result = await this.Post();

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(TestData.PaymentId, problem.Extensions["paymentId"]);
        Assert.Equal(status.ToString(), problem.Extensions["paymentStatus"]);
    }

    // stands in for MVC's factory, which needs the full service provider
    private sealed class SimpleProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null)
        {
            return new ProblemDetails { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
        }

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null)
        {
            return new ValidationProblemDetails(modelStateDictionary) { Status = statusCode ?? StatusCodes.Status400BadRequest };
        }
    }
}
