using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;

namespace PaymentGatewayApi.Controllers;

[Route("api/payments")]
[ApiController]
public class PaymentController : ControllerBase
{
    private readonly ILogger<PaymentController> logger;
    private readonly IPaymentsRepository paymentRepo;
    private readonly IValidationOrchestrator validator;
    private readonly IPaymentProcessor paymentProcessor;

    public PaymentController(
        ILogger<PaymentController> logger, IPaymentsRepository paymentRepo, IValidationOrchestrator validator, IPaymentProcessor paymentProcessor)
    {
        this.logger = logger;
        this.paymentRepo = paymentRepo;
        this.validator = validator;
        this.paymentProcessor = paymentProcessor;
    }

    [HttpGet("{paymentId:guid}")]
    [ProducesResponseType<MerchantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<MerchantResponse>> GetPayment(Guid paymentId)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Started");

        // would have a filtering by merchant, but out of scope for now
        var payment = await this.paymentRepo.Get(paymentId.ToString());
        if (payment is null)
        {
            return this.Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Payment '{paymentId}' was not found.");
        }

        MerchantResponse response = new()
        {
            PaymentId = payment.PaymentId,
            PaymentStatus = payment.Status,
            Amount = payment.Amount,
            CurrencyCode = payment.Currency
        };
        return new OkObjectResult(response);
    }

    [HttpPost]
    [ProducesResponseType<MerchantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<MerchantResponse>> PostPayment([FromBody] PaymentRequest payment)
    {
        // not idempotent for simplicity. Otherwise a merchant would supply a unique payment reference
        payment.PaymentId = Guid.NewGuid().ToString();
        // ISO 4217 codes are upper case; normalized here so the bank and the stored record never see "gbp"
        payment.Currency = payment.Currency.ToUpperInvariant();
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "PostPayment", "Started");

        List<ValidationError> errors = this.validator.ValidatePayment(payment);
        if (errors.Count > 0)
        {
            foreach (ValidationError error in errors)
            {
                this.ModelState.AddModelError(error.Field, error.ErrorMessage);
            }

            return this.ValidationProblem();
        }

        PaymentDb paymentResponse = await this.paymentProcessor.ExecutePayment(payment);
        MerchantResponse merchantResponse = new()
        {
            PaymentId = payment.PaymentId,
            PaymentStatus = paymentResponse.Status,
            Amount = paymentResponse.Amount,
            CurrencyCode = paymentResponse.Currency
        };

        return this.CreatedAtAction(nameof(this.GetPayment), new { paymentId = merchantResponse.PaymentId }, merchantResponse);
    }
}
