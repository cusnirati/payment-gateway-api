using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;

namespace PaymentGatewayApi.Controllers;

[ApiController]
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

    // curl http://localhost:5067/api/payments/16bd4749-d150-48d1-a3ce-257457d74d1f
    [HttpGet("{paymentId:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        this.LogGetPayment(paymentId, "Started");

        Payment? payment = await this.paymentService.FindPayment(paymentId, cancellationToken);
        if (payment is null)
        {
            this.LogGetPayment(paymentId, "NotFound");
            return this.NotFound();
        }

        return PaymentResponse.From(payment);
    }

    // curl -X POST http://localhost:5067/api/payments -H "Content-Type: application/json" \
    //   -d '{"cardNumber":"2222405343248873","expiryMonth":4,"expiryYear":2030,"currency":"GBP","amount":1050,"cvv":"123"}'
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<PaymentResponse>> PostPayment(PaymentRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string[]> errors = this.validator.Validate(request);
        if (errors.Count > 0)
        {
            // no payment is created for an invalid request, so the bank is never called
            return this.ValidationProblem(ToModelState(errors));
        }

        Payment payment = await this.paymentService.ProcessPayment(request, cancellationToken);

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
            ? "The acquiring bank did not process the payment and the card was not charged. It is safe to retry."
            : "The acquiring bank's answer was not received, so the card may have been charged. Check the payment before retrying.";

        ProblemDetails problem = this.ProblemDetailsFactory.CreateProblemDetails(
            this.HttpContext, StatusCodes.Status502BadGateway, "The acquiring bank could not complete the payment.", detail: detail);
        problem.Extensions["paymentId"] = payment.Id;
        problem.Extensions["paymentStatus"] = payment.Status.ToString();

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status502BadGateway };
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
