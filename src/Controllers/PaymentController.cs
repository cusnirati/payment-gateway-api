using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;

namespace PaymentGatewayApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentController : ControllerBase
{
    private readonly ILogger<PaymentController> logger;
    private readonly IPaymentsRepository paymentRepo;
    private readonly IValidationOrchestrator validator;
    private readonly IBankService bankService;

    public PaymentController(
        ILogger<PaymentController> logger, IPaymentsRepository paymentRepo, IValidationOrchestrator validator, IBankService bankService)
    {
        this.logger = logger;
        this.paymentRepo = paymentRepo;
        this.validator = validator;
        this.bankService = bankService;
    }

    // curl -k http://localhost:5067/api/payment
    [HttpGet()]
    public ActionResult<string> GetHealthcheck()
    {
        this.logger.LogInformation("Operation={Operation}, Status={Status}", "GetHealthcheck", "Started");

        return new OkObjectResult("ok");
    }

    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(Guid paymentId)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Started");
        MerchantResponse response = new();

        var payment = await this.paymentRepo.Get(paymentId.ToString());
        if (payment is null)
        {
            response.PaymentStatus = PaymentStatus.NotFound;
            return new OkObjectResult(response);
        }

        response.PaymentId = payment.PaymentId;
        response.PaymentStatus = payment.Status;

        return new OkObjectResult(response);
    }

    [HttpPost()]
    public async Task<ActionResult<MerchantResponse>> PostPayment([FromBody] PaymentRequest payment)
    {
        // not idempotent for simplicity. Otherwise a merchant would supply a unique payment reference
        payment.PaymentId ??= Guid.NewGuid().ToString();

        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "PostPayment", "Started");

        var validationErrors = this.validator.ValidatePayment(payment);
        if (validationErrors.Any())
        {
            return new OkObjectResult(new MerchantResponse { PaymentStatus = PaymentStatus.Rejected, ValidationErrors = validationErrors });
        }

        PostPaymentResponse paymentResponse = await this.bankService.ExecutePayment(payment);
        MerchantResponse response = new() { PaymentId = paymentResponse.PaymentId, PaymentStatus = paymentResponse.Status };

        return new OkObjectResult(response);
    }
}

