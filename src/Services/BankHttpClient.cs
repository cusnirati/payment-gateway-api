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
    private readonly IHttpClientFactory httpFactory;

    public BankHttpClient(ILogger<BankHttpClient> logger, IConfiguration config, IHttpClientFactory httpFactory)
    {
        this.logger = logger;
        this.config = config;
        this.httpFactory = httpFactory;
    }

    public async Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment)
    {
        // "04/2025"
        string expiry = $"{payment.ExpiryMonth}/{payment.ExpiryYear}";
        BankRequest request = new()
        {
            CardNumber = payment.CardNumber,
            ExpiryDate = expiry,
            Currency = payment.Currency,
            Amount = payment.Amount,
            Cvv = payment.Cvv
        };

        string uri = this.config["SimulatorBankEndpoint"] ?? string.Empty;
        HttpClient client = this.httpFactory.CreateClient();

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(uri, request);
        }
        catch (Exception exception)
        {
            // bank unreachable, timed out, or circuit open once the resilience handler gave up;
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
