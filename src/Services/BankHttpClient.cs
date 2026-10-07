using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services;

public interface IBankHttpClient
{
    Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment);
}

public class BankHttpClient : IBankHttpClient
{
    private readonly ILogger<BankHttpClient> logger;
    private readonly IConfiguration config;
    private readonly HttpClient httpClient;

    public BankHttpClient(ILogger<BankHttpClient> logger, IConfiguration config, HttpClient httpClient)
    {
        this.logger = logger;
        this.config = config;
        this.httpClient = httpClient;
    }

    public async Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment)
    {
        // "04/2025"
        string expiry = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
        BankRequest request = new()
        {
            CardNumber = payment.CardNumber,
            ExpiryDate = expiry,
            Currency = payment.Currency,
            Amount = payment.Amount,
            Cvv = payment.Cvv,
            PaymentId = payment.PaymentId 
        };

        HttpResponseMessage response;
        try
        {
            // move to Program.cs
            string bankUrl = this.config["SimulatorBankBaseAddress"] ?? throw new ConfigurationException("SimulatorBankBaseAddress");
            this.httpClient.BaseAddress = new Uri(bankUrl);

            // we assume the bank call is idempotent - providing the same PaymentId
            response = await this.httpClient.PostAsJsonAsync("payments", request);
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "PaymentId={PaymentId}, Operation={Operation}, Status={Status}", payment.PaymentId, "PostBankPayment", "Failed");
            
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        this.logger.LogInformation(
            "PaymentId={PaymentId}, Operation={Operation}, StatusCode={StatusCode}",
            payment.PaymentId, "PostBankPayment", (int)response.StatusCode);

        return response;
    }
}
