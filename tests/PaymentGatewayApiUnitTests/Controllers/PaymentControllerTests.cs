using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private const string PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d";

    private readonly Mock<ILogger<PaymentController>> logger = new();
    private readonly Mock<IPaymentsRepository> repository = new();
    private readonly Mock<IPaymentRule> firstRule = new();
    private readonly Mock<IPaymentRule> secondRule = new();
    private readonly Mock<IPaymentProcessor> processor = new();
    private readonly PaymentController controller;
    private readonly PaymentRequest payment = TestPayments.Create();

    private readonly PaymentDb authorized = new()
    {
        PaymentId = PaymentId,
        CardNumberLastFour = "0007",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Amount = 100,
        Currency = "GBP",
        Status = PaymentStatus.Authorized,
        BankAuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
    };

    public PaymentControllerTests()
    {
        this.controller = new PaymentController(
            this.logger.Object, this.repository.Object, [this.firstRule.Object, this.secondRule.Object], this.processor.Object);

        // Problem() and ValidationProblem() build their bodies with MVC's ProblemDetailsFactory and ApiBehaviorOptions
        ServiceCollection services = new();
        services.AddLogging();
        services.AddControllers();
        this.controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }
        };
    }

    [Fact]
    public async Task GetPayment_StoredPayment_Returns200WithTheMerchantResponse()
    {
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync(this.authorized);

        ActionResult<MerchantResponse> result = await this.controller.GetPayment(Guid.Parse(PaymentId));

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        MerchantResponse response = Assert.IsType<MerchantResponse>(ok.Value);
        Assert.Equivalent(
            new MerchantResponse { PaymentId = PaymentId, PaymentStatus = PaymentStatus.Authorized, Amount = 100, CurrencyCode = "GBP" },
            response,
            strict: true);
    }

    [Fact]
    public async Task GetPayment_UnknownPayment_Returns404Problem()
    {
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync((PaymentDb?)null);

        ActionResult<MerchantResponse> result = await this.controller.GetPayment(Guid.Parse(PaymentId));

        ObjectResult notFound = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        ProblemDetails problem = Assert.IsAssignableFrom<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal($"Payment '{PaymentId}' was not found.", problem.Detail);
    }

    [Fact]
    public async Task PostPayment_NewValidPayment_ProcessesItAndReturns201PointingAtGetPayment()
    {
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync((PaymentDb?)null);
        this.processor.Setup(p => p.ExecutePayment(this.payment)).ReturnsAsync(this.authorized);

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(this.payment);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(PaymentController.GetPayment), created.ActionName);
        Assert.Equal(PaymentId, created.RouteValues?["paymentId"]);
        Assert.Equivalent(
            new MerchantResponse { PaymentId = PaymentId, PaymentStatus = PaymentStatus.Authorized, Amount = 100, CurrencyCode = "GBP" },
            created.Value,
            strict: true);
        this.firstRule.Verify(r => r.Validate(this.payment), Times.Once);
        this.secondRule.Verify(r => r.Validate(this.payment), Times.Once);
        this.processor.Verify(p => p.ExecutePayment(this.payment), Times.Once);
    }

    [Fact]
    public async Task PostPayment_LowerCaseCurrency_IsUpperCasedBeforeValidationAndTheBank()
    {
        this.payment.Currency = "gbp";
        string? validatedCurrency = null;
        this.firstRule.Setup(r => r.Validate(It.IsAny<PaymentRequest>())).Callback<PaymentRequest>(p => validatedCurrency = p.Currency);
        this.processor.Setup(p => p.ExecutePayment(It.IsAny<PaymentRequest>())).ReturnsAsync(this.authorized);

        await this.controller.PostPayment(this.payment);

        Assert.Equal("GBP", validatedCurrency);
        this.processor.Verify(p => p.ExecutePayment(It.Is<PaymentRequest>(r => r.Currency == "GBP")), Times.Once);
    }

    [Fact]
    public async Task PostPayment_RulesFail_Returns400WithEveryErrorAndNeverProcessesThePayment()
    {
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync((PaymentDb?)null);
        this.firstRule.Setup(r => r.Validate(this.payment)).Returns(new ValidationError { Field = "CardNumber", ErrorMessage = "first" });
        this.secondRule.Setup(r => r.Validate(this.payment)).Returns(new ValidationError { Field = "Amount", ErrorMessage = "second" });

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(this.payment);

        ObjectResult badRequest = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(new[] { "first" }, problem.Errors["CardNumber"]);
        Assert.Equal(new[] { "second" }, problem.Errors["Amount"]);
        this.processor.Verify(p => p.ExecutePayment(It.IsAny<PaymentRequest>()), Times.Never);
    }

    [Fact]
    public async Task PostPayment_TwoErrorsOnOneField_ReturnsBothUnderThatField()
    {
        this.firstRule.Setup(r => r.Validate(this.payment)).Returns(new ValidationError { Field = "CardNumber", ErrorMessage = "first" });
        this.secondRule.Setup(r => r.Validate(this.payment)).Returns(new ValidationError { Field = "CardNumber", ErrorMessage = "second" });

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(this.payment);

        ObjectResult badRequest = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(new[] { "CardNumber" }, problem.Errors.Keys);
        Assert.Equal(new[] { "first", "second" }, problem.Errors["CardNumber"]);
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    public async Task PostPayment_PaymentIdAlreadyFinal_ReplaysTheStoredPaymentWithoutValidatingOrProcessing(PaymentStatus status)
    {
        this.authorized.Status = status;
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync(this.authorized);
        // a different body under the same PaymentId still gets the stored payment back
        this.payment.Amount = 999;

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(this.payment);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        MerchantResponse response = Assert.IsType<MerchantResponse>(created.Value);
        Assert.Equal(status, response.PaymentStatus);
        Assert.Equal(100, response.Amount);
        this.firstRule.Verify(r => r.Validate(It.IsAny<PaymentRequest>()), Times.Never);
        this.secondRule.Verify(r => r.Validate(It.IsAny<PaymentRequest>()), Times.Never);
        this.processor.Verify(p => p.ExecutePayment(It.IsAny<PaymentRequest>()), Times.Never);
    }

    [Fact]
    public async Task PostPayment_PaymentIdStoredAsUnavailable_ValidatesAndProcessesItAgain()
    {
        PaymentDb unavailable = new() { PaymentId = PaymentId, Amount = 100, Currency = "GBP", Status = PaymentStatus.Unavailable };
        this.repository.Setup(r => r.Get(PaymentId)).ReturnsAsync(unavailable);
        this.processor.Setup(p => p.ExecutePayment(this.payment)).ReturnsAsync(this.authorized);

        ActionResult<MerchantResponse> result = await this.controller.PostPayment(this.payment);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(PaymentStatus.Authorized, Assert.IsType<MerchantResponse>(created.Value).PaymentStatus);
        this.firstRule.Verify(r => r.Validate(this.payment), Times.Once);
        this.processor.Verify(p => p.ExecutePayment(this.payment), Times.Once);
    }

    [Fact]
    public async Task PostPayment_RepositoryFails_LetsTheExceptionReachTheExceptionHandler()
    {
        InvalidOperationException exception = new("DynamoDB is unreachable");
        this.repository.Setup(r => r.Get(PaymentId)).ThrowsAsync(exception);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => this.controller.PostPayment(this.payment));

        Assert.Same(exception, thrown);
        this.processor.Verify(p => p.ExecutePayment(It.IsAny<PaymentRequest>()), Times.Never);
    }
}
