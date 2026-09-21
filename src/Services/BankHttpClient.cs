using System;
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
        // get back why
        string bankEndpoint = this.config["SimulatorBankEndpoint"] ?? string.Empty;

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

        // using simple httpClient for dev purpose
        // ideally a fine tuned library or use a clientFactory in Program.cs
        // same goes for retries, timeouts
        // https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#recommended-use

        // todo exception catch
        // see if using needed

        // using HttpClient client = new();
        // client.BaseAddress = new Uri(bankEndpoint);


        HttpClient client = this.httpFactory.CreateClient();
        string uri = $"{bankEndpoint}/payments";

        HttpResponseMessage response = await client.PostAsJsonAsync(uri, request);

        this.logger.LogInformation(
            "PostBankPayment, PaymentId={PaymentId}, StatusCode={StatusCode}",
            payment.PaymentId, (int)response.StatusCode);

        // todo check statusCode

        return response;
    }
}
