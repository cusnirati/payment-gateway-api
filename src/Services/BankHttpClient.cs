using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace PaymentGatewayApi.Services;

public interface IBankHttpClient
{
    Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment);
}



public class BankHttpClient : IBankHttpClient
{
    private readonly ILogger<BankHttpClient> logger;
    private readonly IConfiguration config;
    private readonly HttpClient client;

    public BankHttpClient(ILogger<BankHttpClient> logger, IConfiguration config, HttpClient client)
    {
        this.logger = logger;
        this.config = config;
        this.client = client;
    }

    public static BankRequest ToBankRequest(PaymentRequest payment)
    {
        return new BankRequest
        {
            // the bank expects "MM/yyyy", e.g. "04/2025"
            CardNumber = payment.CardNumber,
            ExpiryDate = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}",
            Currency = payment.Currency,
            Amount = payment.Amount,
            Cvv = payment.Cvv
        };
    }

    public async Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment)
    {
        BankRequest request = ToBankRequest(payment);
        string uri = this.config["SimulatorBankEndpoint"] ?? string.Empty;

        HttpResponseMessage response;
        try
        {
            response = await this.client.PostAsJsonAsync(uri, request);
        }
        catch (Exception exception)
        {
            // bank unreachable, timed out, or circuit open;
            // answer as the bank would when unavailable so BankService maps it to InternalError
            this.logger.LogError(exception, "PostBankPayment, PaymentId={PaymentId}, Status={Status}", payment.PaymentId, "Failed");

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        this.logger.LogInformation(
            "PostBankPayment, PaymentId={PaymentId}, StatusCode={StatusCode}",
            payment.PaymentId, (int)response.StatusCode);

        return response;
    }
}
