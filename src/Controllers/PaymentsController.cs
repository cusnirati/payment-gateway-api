using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Security;
using PaymentGatewayApi.Services;

namespace PaymentGatewayApi.Controllers;

[ApiController]
[Authorize]
[Route("api/payments")]
public partial class PaymentsController : ControllerBase
{
    private readonly ILogger<PaymentsController> logger;
    private readonly IPaymentRequestValidator validator;
    private readonly IPaymentService paymentService;

    public PaymentsController(ILogger<PaymentsController> logger, IPaymentRequestValidator validator, IPaymentService paymentService)
    {
        this.logger = logger;
        this.validator = validator;
        this.paymentService = paymentService;
    }

    // curl http://localhost:5067/api/payments/16bd4749-d150-48d1-a3ce-257457d74d1f -H "X-Api-Key: dev-key-merchant-a"
    [HttpGet("{paymentId:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        this.LogGetPayment(paymentId, "Started");

        Payment? payment = await this.paymentService.FindPayment(this.User.GetMerchantId(), paymentId, cancellationToken);
        if (payment is null)
        {
            this.LogGetPayment(paymentId, "NotFound");
            return this.NotFound();
        }

        return PaymentResponse.From(payment);
    }

    // curl -X POST http://localhost:5067/api/payments -H "X-Api-Key: dev-key-merchant-a" -H "Content-Type: application/json" \
    //   -d '{"cardNumber":"2222405343248873","expiryMonth":4,"expiryYear":2030,"currency":"GBP","amount":1050,"cvv":"123"}'
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.PaymentsPerMerchant)]
    [Consumes("application/json")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<PaymentResponse>> PostPayment(
        PaymentRequest request,
        [FromHeader(Name = IdempotencyRecord.HeaderName)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> errors = new(this.validator.Validate(request));
        if (idempotencyKey is not null && !IdempotencyRecord.IsValidKey(idempotencyKey))
        {
            errors[IdempotencyRecord.HeaderName] = ["Must be 1 to 255 visible ASCII characters."];
        }

        if (errors.Count > 0)
        {
            // no payment is created for an invalid request, so the bank is never called
            return this.ValidationProblem(ToModelState(errors));
        }

        PaymentAttempt attempt = await this.paymentService.ProcessPayment(
            this.User.GetMerchantId(), request, idempotencyKey, cancellationToken);

        switch (attempt)
        {
            case PaymentAttempt.Processed processed:
                return this.PaymentResult(processed.Payment);

            case PaymentAttempt.Replayed replayed:
                // the same answer as the first request with this key, marked so the merchant can tell
                this.Response.Headers["Idempotent-Replayed"] = "true";
                return this.PaymentResult(replayed.Payment);

            case PaymentAttempt.InProgress inProgress:
                ObjectResult conflict = this.ProblemResult(
                    StatusCodes.Status409Conflict,
                    "A payment with this Idempotency-Key is still being processed.",
                    "Retry the same request later to get its outcome.");
                ((ProblemDetails)conflict.Value!).Extensions["paymentId"] = inProgress.PaymentId;
                return conflict;

            default:
                return this.ProblemResult(
                    StatusCodes.Status422UnprocessableEntity,
                    "This Idempotency-Key was already used for a different payment.",
                    "Use a new key for a new payment.");
        }
    }

    private ActionResult<PaymentResponse> PaymentResult(Payment payment)
    {
        // a declined payment is a valid outcome, the issuer said no: it is created like an authorized one
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Declined)
        {
            return this.CreatedAtAction(nameof(this.GetPayment), new { paymentId = payment.Id }, PaymentResponse.From(payment));
        }

        return this.BankProblem(payment);
    }

    // the bank, not the merchant, is at fault, so never a 4xx (which tells a client not to retry)
    private ObjectResult BankProblem(Payment payment)
    {
        string detail = payment.Status == PaymentStatus.Failed
            ? "The acquiring bank did not process the payment and the card was not charged. It is safe to retry as a new payment, with a new Idempotency-Key if one was sent."
            : "The acquiring bank's answer was not received, so the card may have been charged. Check the payment before retrying.";

        ObjectResult result = this.ProblemResult(StatusCodes.Status502BadGateway, "The acquiring bank could not complete the payment.", detail);
        ProblemDetails problem = (ProblemDetails)result.Value!;
        problem.Extensions["paymentId"] = payment.Id;
        problem.Extensions["paymentStatus"] = payment.Status.ToString();

        return result;
    }

    // like ControllerBase.Problem, but the caller can still add extensions to the ProblemDetails
    private ObjectResult ProblemResult(int statusCode, string title, string detail)
    {
        ProblemDetails problem = this.ProblemDetailsFactory.CreateProblemDetails(this.HttpContext, statusCode, title, detail: detail);
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static ModelStateDictionary ToModelState(IReadOnlyDictionary<string, string[]> errors)
    {
        ModelStateDictionary modelState = new();
        foreach ((string field, string[] messages) in errors)
        {
            foreach (string message in messages)
            {
                modelState.AddModelError(field, message);
            }
        }

        return modelState;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogGetPayment(Guid paymentId, string status);
}
