using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;

namespace PaymentGatewayApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public partial class PaymentController : ControllerBase
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
        this.LogGetHealthcheck("Started");

        return new OkObjectResult("ok");
    }

    // curl -k http://localhost:5067/api/payment/16bd4749-d150-48d1-a3ce-257457d74d1f
    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(string paymentId)
    {
        this.LogGetPayment(paymentId, "Started");

        var payment = await this.paymentRepo.Find(paymentId);
        if (payment is null)
        {
            return new NotFoundObjectResult("payment not found");
        }

        return new OkObjectResult(payment);
    }

    [HttpPost()]
    public async Task<ActionResult<PaymentStatus>> PostPayment([FromBody] PaymentRequest payment)
    {
        payment.PaymentId = Guid.NewGuid().ToString();
        this.LogPostPayment(payment.PaymentId, "Started");

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

        var status = await this.bankService.ExecutePayment(payment);
        if (status == PaymentStatus.Authorized)
        {
            return new OkObjectResult(payment.PaymentId);
        }

        return new BadRequestObjectResult(status.ToString());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GetHealthcheck, Status={Status}")]
    private partial void LogGetHealthcheck(string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogGetPayment(string paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogPostPayment(string? paymentId, string status);
}
