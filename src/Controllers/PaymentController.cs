using System;
using System.Threading;
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
    private readonly IPaymentRequestValidator validator;
    private readonly IPaymentService paymentService;

    public PaymentController(ILogger<PaymentController> logger, IPaymentRequestValidator validator, IPaymentService paymentService)
    {
        this.logger = logger;
        this.validator = validator;
        this.paymentService = paymentService;
    }

    // curl -k http://localhost:5067/api/payment
    [HttpGet]
    public ActionResult<string> GetHealthcheck()
    {
        this.LogGetHealthcheck("Started");

        return new OkObjectResult("ok");
    }

    // curl -k http://localhost:5067/api/payment/16bd4749-d150-48d1-a3ce-257457d74d1f
    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PostPaymentResponse>> GetPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        this.LogGetPayment(paymentId, "Started");

        Payment? payment = await this.paymentService.FindPayment(paymentId, cancellationToken);
        if (payment is null)
        {
            return new NotFoundObjectResult("payment not found");
        }

        return new OkObjectResult(PostPaymentResponse.From(payment));
    }

    [HttpPost]
    public async Task<ActionResult<PaymentStatus>> PostPayment([FromBody] PaymentRequest request, CancellationToken cancellationToken)
    {
        this.LogPostPayment("Started");

        bool isValid =
            this.validator.IsCardNumberValid(request) &&
            this.validator.IsExpiryValid(request) &&
            this.validator.IsAmountValid(request) &&
            this.validator.IsCurrencyCodeValid(request) &&
            this.validator.IsCvvValid(request);

        if (!isValid)
        {
            return new BadRequestObjectResult("Rejected");
        }

        Payment payment = await this.paymentService.ProcessPayment(request, cancellationToken);
        if (payment.Status == PaymentStatus.Authorized)
        {
            return new OkObjectResult(payment.Id.ToString());
        }

        return new BadRequestObjectResult(payment.Status.ToString());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GetHealthcheck, Status={Status}")]
    private partial void LogGetHealthcheck(string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogGetPayment(Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostPayment, Status={Status}")]
    private partial void LogPostPayment(string status);
}
