using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;

namespace PaymentGatewayApi.Services;

public interface IBankHttpClient
{
    Task<BankResponse?> PostPayment(PaymentRequest payment);
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

    public async Task<BankResponse?> PostPayment(PaymentRequest payment)
    {
        try
        {
            // "04/2025"
            string expiry = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
            BankRequest request = new()
            {
                CardNumber = payment.CardNumber,
                Cvv = payment.Cvv,
                ExpiryDate = expiry,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentId = payment.PaymentId
            };

            string bankUrl = this.config["SimulatorBankBaseAddress"] ?? throw new ConfigurationException("SimulatorBankBaseAddress");
            this.httpClient.BaseAddress = new Uri(bankUrl);

            // we assume the bank call is idempotent - providing the same PaymentId
            using HttpResponseMessage httpResponse = await this.httpClient.PostAsJsonAsync("payments", request);
            httpResponse.EnsureSuccessStatusCode();

            BankResponse? bankResponse = await httpResponse.Content.ReadFromJsonAsync<BankResponse>();
            return bankResponse;
        }
        catch (Exception exception)
        {
            this.logger.LogError(
                exception,
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "PostBankPayment", "Failed");
        }

        return null;
    }
}
