using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IBankService
{
    Task<PaymentStatus> ExecutePayment(PaymentRequest payment, CancellationToken cancellationToken);
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

    public async Task<PaymentStatus> ExecutePayment(PaymentRequest payment, CancellationToken cancellationToken)
    {
        this.LogExecutePayment(payment.PaymentId, "Started");

        // a client that disconnects can still stop the payment before the bank is called; once it is,
        // cancelling would leave the bank's outcome unknown, so the bank call and the write that
        // records its answer run to completion (bounded by the resilience handler's timeouts)
        cancellationToken.ThrowIfCancellationRequested();

        HttpResponseMessage bankResponse = await this.bankHttpClient.PostBankPayment(payment, CancellationToken.None);
        if (bankResponse.IsSuccessStatusCode)
        {
            PostPaymentResponse? paymentResponse = await this.responseProcessor.ParseBankResponse(payment, bankResponse, CancellationToken.None);
            if (paymentResponse is null)
            {
                // the bank answered 2xx but its body is unusable, so the outcome is unknown and there is nothing to persist
                this.LogExecutePaymentFailed(payment.PaymentId, "Failed");
                return PaymentStatus.InternalError;
            }

            await this.paymentRepo.Add(paymentResponse, CancellationToken.None);

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
