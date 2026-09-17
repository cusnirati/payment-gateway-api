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

    public BankHttpClient(ILogger<BankHttpClient> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
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

        using HttpClient client = new();
        client.BaseAddress = new Uri(bankEndpoint);
        HttpResponseMessage response = await client.PostAsJsonAsync($"/payments", request);

        // todo double check log format. no paymentId
        this.logger.LogInformation("PostBankPayment.Completed, HttpStatusCode={HttpStatusCode}", (int)response.StatusCode);

        return response;
    }
}
