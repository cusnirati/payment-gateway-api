using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentController : ControllerBase
{
    private readonly ILogger<PaymentController> logger;
    private readonly IPaymentsRepository paymentRepo;
    private readonly IPaymentRequestValidator validator;
    private readonly IBankService bankService;

    public PaymentController(
        ILogger<PaymentController> logger, IPaymentsRepository paymentRepo, IPaymentRequestValidator validator, IBankService bankService)
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

    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(string paymentId)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Started");

        var payment = await this.paymentRepo.Get(paymentId);
        if (payment is null)
        {
            return new NotFoundObjectResult("payment not found");
        }

        return new OkObjectResult(payment);
    }

    [HttpPost()]
    public async Task<ActionResult<PostPaymentResponse>> PostPayment([FromBody] PaymentRequest payment)
    {
        // not idempotent for simplicity
        payment.PaymentId = Guid.NewGuid().ToString();
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "PostPayment", "Started");

        bool isValid =
            this.validator.IsCardNumberValid(payment) &&
            this.validator.IsExpiryValid(payment, DateTime.Now) &&
            this.validator.IsAmountValid(payment) &&
            this.validator.IsCurrencyCodeValid(payment) &&
            this.validator.IsCvvValid(payment);

        if (!isValid)
        {
            return new BadRequestObjectResult(PaymentStatus.Rejected.ToString());
        }

        // good place to split into a separate service if validation would include fraud detection, sanction screening, etc

        // todo sync consistent merchant responses
        var paymentResponse = await this.bankService.ExecutePayment(payment);
        if (paymentResponse.Status == PaymentStatus.Authorized || paymentResponse.Status == PaymentStatus.Declined)
        {
            return new OkObjectResult(paymentResponse);
        }

        return new BadRequestObjectResult(paymentResponse.Status.ToString());
    }
}
