using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;

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

    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(Guid paymentId)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentId, "GetPayment", "Started");
        MerchantResponse response = new();

        var payment = await this.paymentRepo.Get(paymentId.ToString());
        if (payment is null)
        {
            response.Status = PaymentStatus.NotFound;
            return new OkObjectResult(response);
        }

        response.PaymentId = payment.PaymentId;
        response.Status = payment.Status;

        return new OkObjectResult(response);
    }

    [HttpPost()]
    public async Task<ActionResult<MerchantResponse>> PostPayment([FromBody] PaymentRequest payment)
    {
        // not idempotent for simplicity. Otherwise a merchant would supply a unique payment reference
        payment.PaymentId ??= Guid.NewGuid().ToString();

        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "PostPayment", "Started");
        bool isValid =
            this.validator.IsCardNumberValid(payment) &&
            this.validator.IsExpiryValid(payment, DateTime.Now) &&
            this.validator.IsAmountValid(payment) &&
            this.validator.IsCurrencyCodeValid(payment) &&
            this.validator.IsCvvValid(payment);

        if (!isValid)
        {
            // todo add validation message
            return new OkObjectResult(new MerchantResponse { Status = PaymentStatus.Rejected });
        }

        PostPaymentResponse paymentResponse = await this.bankService.ExecutePayment(payment);

        var response = new MerchantResponse { PaymentId = paymentResponse.PaymentId, Status = paymentResponse.Status };
        return new OkObjectResult(response);
    }
}

public class MerchantResponse
{
    public string? PaymentId { get; set; }
    public PaymentStatus Status { get; set; }

    public ValidationMessage ValidationMessage { get; set; }
}

// or RFC 9457. https://www.rfc-editor.org/info/rfc9457/
[JsonConverter(typeof(JsonStringEnumConverter<ValidationMessage>))]
public enum ValidationMessage
{
    InvalidCardNumber,
    InvalidExpiry
}
