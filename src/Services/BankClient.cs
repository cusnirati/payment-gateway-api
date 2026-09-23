using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using Polly.CircuitBreaker;

namespace PaymentGatewayApi.Services;

public interface IBankClient
{
    Task<BankOutcome> Authorize(Guid paymentId, PaymentRequest payment, CancellationToken cancellationToken);
}

// owns the acquiring bank's contract: the request shape, and turning every answer (or its absence) into a BankOutcome
public partial class BankClient : IBankClient
{
    private static readonly Uri PaymentsUri = new("payments", UriKind.Relative);

    private readonly ILogger<BankClient> logger;
    private readonly HttpClient client;

    // the base address comes from BankOptions, set where the typed client is registered
    public BankClient(ILogger<BankClient> logger, HttpClient client)
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

    public async Task<BankOutcome> Authorize(Guid paymentId, PaymentRequest payment, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await this.client.PostAsJsonAsync(PaymentsUri, ToBankRequest(payment), cancellationToken);
        }
        catch (BrokenCircuitException exception)
        {
            // the circuit breaker short-circuited the call, the request never left the gateway
            this.LogBankUnavailable(exception, paymentId, "Failed");
            return BankOutcome.Failed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // timed out or the connection dropped: the bank may have received and authorized the payment
            this.LogBankUnavailable(exception, paymentId, "Unknown");
            return BankOutcome.Unknown;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return this.FromErrorStatus(paymentId, response.StatusCode);
            }

            BankResponse? bankResponse;
            try
            {
                bankResponse = await response.Content.ReadFromJsonAsync<BankResponse>(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                this.LogBankAnswerUnreadable(exception, paymentId, "Unknown");
                return BankOutcome.Unknown;
            }

            if (bankResponse is null)
            {
                this.LogBankAnswerUnreadable(null, paymentId, "Unknown");
                return BankOutcome.Unknown;
            }

            this.LogBankAnswered(paymentId, "Completed", bankResponse.Authorized);

            return bankResponse.Authorized ? BankOutcome.Authorized(bankResponse.AuthorizationCode) : BankOutcome.Declined;
        }
    }

    private BankOutcome FromErrorStatus(Guid paymentId, HttpStatusCode statusCode)
    {
        // a 4xx means the bank rejected the request and a 503 that it did not process it, so nothing was charged;
        // any other 5xx says nothing reliable about whether the payment went through
        bool notCharged = (int)statusCode < 500 || statusCode == HttpStatusCode.ServiceUnavailable;
        BankOutcome outcome = notCharged ? BankOutcome.Failed : BankOutcome.Unknown;

        this.LogBankError(paymentId, outcome.Status.ToString(), (int)statusCode);
        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Authorize, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogBankUnavailable(Exception exception, Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Authorize, PaymentId={PaymentId}, Status={Status}, StatusCode={StatusCode}")]
    private partial void LogBankError(Guid paymentId, string status, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Authorize, PaymentId={PaymentId}, Status={Status}, the bank's answer could not be read")]
    private partial void LogBankAnswerUnreadable(Exception? exception, Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Authorize, PaymentId={PaymentId}, Status={Status}, Authorized={Authorized}")]
    private partial void LogBankAnswered(Guid paymentId, string status, bool authorized);
}
