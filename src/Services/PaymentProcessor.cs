using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;

namespace PaymentGatewayApi.Services;

public interface IPaymentProcessor
{
    Task<PaymentDb> ExecutePayment(PaymentRequest payment);
}

public class PaymentProcessor : IPaymentProcessor
{
    private readonly ILogger<PaymentProcessor> logger;
    private readonly IBankHttpClient bankHttpClient;
    private readonly IBankResponseProcessor responseProcessor;
    private readonly IPaymentsRepository paymentRepository;

    public PaymentProcessor(
        ILogger<PaymentProcessor> logger, IBankHttpClient bankHttpClient, IBankResponseProcessor responseProcessor, IPaymentsRepository paymentRepository)
    {
        this.logger = logger;
        this.bankHttpClient = bankHttpClient;
        this.responseProcessor = responseProcessor;
        this.paymentRepository = paymentRepository;
    }

    public async Task<PaymentDb> ExecutePayment(PaymentRequest paymentRequest)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentRequest.PaymentId, "ExecuteBankPayment", "Started");
        await this.paymentRepository.AddPending(paymentRequest);

        using HttpResponseMessage bankResponse = await this.bankHttpClient.PostBankPayment(paymentRequest);
        if (bankResponse.IsSuccessStatusCode)
        {
            PaymentDb paymentDb = await this.responseProcessor.ParseBankResponse(paymentRequest, bankResponse);
            if (paymentDb.Status == PaymentStatus.InternalError)
            {
                // the processor returns only the status when the body is unusable, keep the pending details instead
                // pending.Status = PaymentStatus.InternalError;
                // paymentResponse = pending;
            }

            await this.paymentRepository.Complete(paymentDb);

            return paymentDb;
        }

        throw new Exception();

        // else
        // {
        //     this.logger.LogError("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentRequest.PaymentId, "ExecuteBankPayment", "Failed");

        //     pending.Status = PaymentStatus.InternalError;
        //     await this.paymentRepository.Complete(pending);

        //     return pending;
        // }
    }
}