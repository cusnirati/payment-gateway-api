using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentController : Controller
{
    private readonly ILogger<PaymentController> logger;
    private readonly IPostPaymentRepository paymentRepo;
    private readonly IPaymentRequestValidator validator;
    private readonly IBankService bankService;

    public PaymentController(
        ILogger<PaymentController> logger, IPostPaymentRepository paymentRepo, IPaymentRequestValidator validator, IBankService bankService)
    {
        this.logger = logger;
        this.paymentRepo = paymentRepo;
        this.validator = validator;
        this.bankService = bankService;
    }

    // curl -k https://localhost:7092/api/payment
    [HttpGet()]
    public async Task<ActionResult<string>> GetHealthcheck()
    {
        this.logger.LogInformation("GetHealthcheck.Received");

        return new OkObjectResult("ok");
    }

    // curl -k https://localhost:7092/api/payment/16bd4749-d150-48d1-a3ce-257457d74d1f
    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(string paymentId)
    {
        this.logger.LogInformation("GetPayment.Received, PaymentId={PaymentId}", paymentId);
        var payment = await this.paymentRepo.Get(paymentId);

        return new OkObjectResult(payment);
    }

    [HttpPost()]
    public async Task<ActionResult<PaymentStatus>> PostPayment([FromBody] PaymentRequest payment)
    {
        payment.PaymentId = Guid.NewGuid().ToString();
        this.logger.LogInformation("PostPayment.Received, PaymentId={PaymentId}", payment.PaymentId);

        bool isValid =
            this.validator.IsCardNumberValid(payment) &&
            this.validator.IsExpiryValid(payment, DateTime.Now) &&
            this.validator.IsAmountValid(payment) &&
            this.validator.IsCurrencyCodeValid(payment) &&
            this.validator.ValidateCvv(payment);

        if (!isValid)
        {
            return new BadRequestObjectResult(PaymentStatus.Rejected.ToString());
        }

        this.logger.LogInformation("PostPayment.ValidationCompleted, PaymentId={PaymentId}", payment.PaymentId);

        // good place to split into a separate service
        // since in validation can be included fraud detection, sanction screening, etc

        PaymentStatus status = await this.bankService.ExecutePayment(payment);
        if (status == PaymentStatus.Authorized)
        {
            return new OkObjectResult(payment.PaymentId);
        }
        else
        {
            // todo. if insufficient funds still return ok
            return new BadRequestObjectResult(status.ToString());
        }
    }
}
