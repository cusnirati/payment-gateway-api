using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IBankResponseProcessor
{
    // null when the bank's body cannot be used, so no half-filled response is ever persisted
    Task<PostPaymentResponse?> ParseBankResponse(PaymentRequest payment, HttpResponseMessage response);
}

public partial class BankResponseProcessor : IBankResponseProcessor
{
    private readonly ILogger<BankResponseProcessor> logger;

    public BankResponseProcessor(ILogger<BankResponseProcessor> logger)
    {
        this.logger = logger;
    }

    public async Task<PostPaymentResponse?> ParseBankResponse(PaymentRequest payment, HttpResponseMessage response)
    {
        BankResponse? bankResponse;

        try
        {
            bankResponse = await response.Content.ReadFromJsonAsync<BankResponse>();
        }
        catch (Exception exception)
        {
            this.LogUnparseable(exception, payment.PaymentId, "Failed");

            return null;
        }

        if (bankResponse is null)
        {
            this.LogUnparseable(null, payment.PaymentId, "Failed");

            return null;
        }

        PostPaymentResponse payResponse = new();
        payResponse.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        payResponse.CardNumberLastFour = payment.CardNumber.Substring(payment.CardNumber.Length - 4);

        payResponse.PaymentId = payment.PaymentId;
        payResponse.ExpiryMonth = payment.ExpiryMonth;
        payResponse.ExpiryYear = payment.ExpiryYear;
        payResponse.Currency = payment.Currency;
        payResponse.Amount = payment.Amount;

        this.LogParsed(payment.PaymentId, "Completed", bankResponse.Authorized);

        return payResponse;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "ParseBankResponse, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogUnparseable(Exception? exception, string? paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "ParseBankResponse, PaymentId={PaymentId}, Status={Status}, Authorized={Authorized}")]
    private partial void LogParsed(string? paymentId, string status, bool authorized);
}
