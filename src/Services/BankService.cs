using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IBankService
{
    Task<PaymentStatus> ExecutePayment(PaymentRequest payment);
}

public partial class BankService : IBankService
{
    private readonly ILogger<BankService> logger;
    private readonly IBankHttpClient bankHttpClient;
    private readonly IBankResponseProcessor responseProcessor;
    private readonly IPaymentsRepository paymentRepo;

    public BankService(
        ILogger<BankService> logger, IBankHttpClient bankHttpClient, IBankResponseProcessor responseProcessor, IPaymentsRepository paymentRepo)
    {
        this.logger = logger;
        this.bankHttpClient = bankHttpClient;
        this.responseProcessor = responseProcessor;
        this.paymentRepo = paymentRepo;
    }

    public async Task<PaymentStatus> ExecutePayment(PaymentRequest payment)
    {
        this.LogExecutePayment(payment.PaymentId, "Started");

        HttpResponseMessage bankResponse = await this.bankHttpClient.PostBankPayment(payment);
        if (bankResponse.IsSuccessStatusCode)
        {
            PostPaymentResponse? paymentResponse = await this.responseProcessor.ParseBankResponse(payment, bankResponse);
            if (paymentResponse is null)
            {
                // the bank answered 2xx but its body is unusable, so the outcome is unknown and there is nothing to persist
                this.LogExecutePaymentFailed(payment.PaymentId, "Failed");
                return PaymentStatus.InternalError;
            }

            await this.paymentRepo.Add(paymentResponse);

            return paymentResponse.Status;
        }
        else
        {
            this.LogExecutePaymentFailed(payment.PaymentId, "Failed");
            return PaymentStatus.InternalError;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ExecutePayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogExecutePayment(string? paymentId, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "ExecutePayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogExecutePaymentFailed(string? paymentId, string status);
}
