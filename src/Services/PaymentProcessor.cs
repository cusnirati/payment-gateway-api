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

        BankResponse? bankResponse = await this.bankHttpClient.PostPayment(paymentRequest);
        PaymentDb paymentDb = await this.responseProcessor.ParseBankResponse(paymentRequest, bankResponse);
        await this.paymentRepository.Complete(paymentDb);

        throw new Exception();
    }
}