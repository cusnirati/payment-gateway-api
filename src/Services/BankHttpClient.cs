using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IBankHttpClient
{
    Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment, CancellationToken cancellationToken);
}

public partial class BankHttpClient : IBankHttpClient
{
    private readonly ILogger<BankHttpClient> logger;
    private readonly HttpClient client;

    // the base address comes from BankOptions, set where the typed client is registered
    public BankHttpClient(ILogger<BankHttpClient> logger, HttpClient client)
    {
        this.logger = logger;
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

    public async Task<HttpResponseMessage> PostBankPayment(PaymentRequest payment, CancellationToken cancellationToken)
    {
        BankRequest request = ToBankRequest(payment);

        HttpResponseMessage response;
        try
        {
            response = await this.client.PostAsJsonAsync(new Uri("payments", UriKind.Relative), request, cancellationToken);
        }
        catch (Exception exception)
        {
            // bank unreachable, timed out, or circuit open;
            // answer as the bank would when unavailable so BankService maps it to InternalError
            this.LogPostFailed(exception, payment.PaymentId, "Failed");

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        this.LogPosted(payment.PaymentId, "Completed", (int)response.StatusCode);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "PostBankPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogPostFailed(Exception exception, string? paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostBankPayment, PaymentId={PaymentId}, Status={Status}, StatusCode={StatusCode}")]
    private partial void LogPosted(string? paymentId, string status, int statusCode);
}
