using System.Net.Http;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Microsoft.Extensions.Logging;
using Serilog;

namespace PaymentGatewayApi.Services;

public interface IBankService
{
    Task<PaymentStatus> ExecutePayment(PaymentRequest payment);
}

public class BankService : IBankService
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
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "ExecuteBankPayment", "Started");

        // record the payment before the bank is called, a failure here throws and the bank is never called
        PostPaymentResponse pending = CreatePending(payment);
        await this.paymentRepo.Add(pending);

        HttpResponseMessage bankResponse = await this.bankHttpClient.PostBankPayment(payment);
        if (bankResponse.IsSuccessStatusCode)
        {
            PostPaymentResponse paymentResponse = await this.responseProcessor.ParseBankResponse(payment, bankResponse);
            if (paymentResponse.Status == PaymentStatus.InternalError)
            {
                // the processor returns only the status when the body is unusable, keep the pending details instead
                pending.Status = PaymentStatus.InternalError;
                paymentResponse = pending;
            }

            await this.paymentRepo.Update(paymentResponse);

            return paymentResponse.Status;
        }
        else
        {
            this.logger.LogError("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "ExecuteBankPayment", "Failed");

            pending.Status = PaymentStatus.InternalError;
            await this.paymentRepo.Update(pending);

            return PaymentStatus.InternalError;
        }
    }

    private static PostPaymentResponse CreatePending(PaymentRequest payment)
    {
        return new PostPaymentResponse
        {
            PaymentId = payment.PaymentId,
            Status = PaymentStatus.Pending,
            CardNumberLastFour = payment.CardNumber.Substring(payment.CardNumber.Length - 4),
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}