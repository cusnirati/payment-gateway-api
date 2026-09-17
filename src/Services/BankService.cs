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
    private readonly IPostPaymentRepository paymentRepo;

    public BankService(
        ILogger<BankService> logger, IBankHttpClient bankHttpClient, IBankResponseProcessor responseProcessor, IPostPaymentRepository paymentRepo)
    {
        this.logger = logger;
        this.bankHttpClient = bankHttpClient;
        this.responseProcessor = responseProcessor;
        this.paymentRepo = paymentRepo;
    }

    public async Task<PaymentStatus> ExecutePayment(PaymentRequest payment)
    {
        this.logger.LogInformation("ExecuteBankPayment.Received, PaymentId={PaymentId}", payment.PaymentId);

        HttpResponseMessage bankResponse = await this.bankHttpClient.PostBankPayment(payment);
        if (bankResponse.IsSuccessStatusCode)
        {
            PostPaymentResponse paymentResponse = await this.responseProcessor.ParseBankResponse(payment, bankResponse);
            // or more complex logic for status != PaymentStatus.Authorized
            await this.paymentRepo.Add(paymentResponse);

            return paymentResponse.Status;
        }
        else
        {
            this.logger.LogError("ExecuteBankPayment.Failed, Status={Status}", "Failed");

            return PaymentStatus.InternalError;
        }
    }
}