using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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


    private readonly HttpClient httpClient;


    public PaymentProcessor(
        ILogger<PaymentProcessor> logger, IBankHttpClient bankHttpClient, IBankResponseProcessor responseProcessor, IPaymentsRepository paymentRepository,
        HttpClient httpClient)
    {
        this.logger = logger;
        this.bankHttpClient = bankHttpClient;
        this.responseProcessor = responseProcessor;
        this.paymentRepository = paymentRepository;


        this.httpClient = httpClient;
    }

    public async Task<PaymentDb> ExecutePayment(PaymentRequest payment)
    {
        this.logger.LogInformation("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "ExecuteBankPayment", "Started");
        await this.paymentRepository.AddPending(payment);

        BankRequest bankRequest = new()
        {
            CardNumber = payment.CardNumber,
            ExpiryDate = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}", // "04/2025"
            Currency = payment.Currency,
            Amount = payment.Amount,
            Cvv = payment.Cvv,
            PaymentId = payment.PaymentId
        };

        this.httpClient.BaseAddress = new Uri("http://localhost:8080");
        HttpResponseMessage response = await this.httpClient.PostAsJsonAsync("payments", bankRequest);

        BankResponse? bankResponse;
        if (response.IsSuccessStatusCode)
        {
            bankResponse = await response.Content.ReadFromJsonAsync<BankResponse>();
            // if throw when parse => payResponse.Status = PaymentStatus.InternalError;
            // same for if null
        }
        else
        {
            throw new Exception();
        }

        PaymentDb payResponse = new();

        payResponse.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        payResponse.CardNumberLastFour = payment.CardNumber.Substring(payment.CardNumber.Length - 4);

        payResponse.PaymentId = payment.PaymentId;
        payResponse.ExpiryMonth = payment.ExpiryMonth;
        payResponse.ExpiryYear = payment.ExpiryYear;
        payResponse.Currency = payment.Currency;
        payResponse.Amount = payment.Amount;
        payResponse.BankAuthorizationCode = bankResponse.AuthorizationCode;

        return payResponse;












        // if (bankResponse.IsSuccessStatusCode)
        // {
        //     PaymentDb paymentDb = await this.responseProcessor.ParseBankResponse(payment, bankResponse);
        //     if (paymentDb.Status == PaymentStatus.InternalError)
        //     {
        //         // the processor returns only the status when the body is unusable, keep the pending details instead
        //         // pending.Status = PaymentStatus.InternalError;
        //         // paymentResponse = pending;
        //     }

        //     await this.paymentRepository.Complete(paymentDb);

        //     return paymentDb;
        // }

        // throw new Exception();

        // else
        // {
        //     this.logger.LogError("PaymentId={PaymentId}, Operation={Operation}, Status={Status}", paymentRequest.PaymentId, "ExecuteBankPayment", "Failed");

        //     pending.Status = PaymentStatus.InternalError;
        //     await this.paymentRepository.Complete(pending);

        //     return pending;
        // }
    }
}