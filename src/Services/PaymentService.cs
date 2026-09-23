using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentService
{
    // takes a validated request through the bank and returns the payment with the bank's outcome
    Task<Payment> ProcessPayment(PaymentRequest request, CancellationToken cancellationToken);

    Task<Payment?> FindPayment(Guid paymentId, CancellationToken cancellationToken);
}

public partial class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> logger;
    private readonly IBankClient bankClient;
    private readonly IPaymentsRepository paymentRepo;
    private readonly TimeProvider timeProvider;

    public PaymentService(
        ILogger<PaymentService> logger, IBankClient bankClient, IPaymentsRepository paymentRepo, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.bankClient = bankClient;
        this.paymentRepo = paymentRepo;
        this.timeProvider = timeProvider;
    }

    public async Task<Payment> ProcessPayment(PaymentRequest request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = this.timeProvider.GetUtcNow();
        Payment pending = new()
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Pending,
            CardNumberLastFour = request.CardNumber[^4..],
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency.ToUpperInvariant(),
            Amount = request.Amount,
            CreatedAt = now,
            UpdatedAt = now
        };

        this.LogProcessPayment(pending.Id, "Started");

        // recorded before the bank is called, so a card can never be charged without a record of it;
        // if this write fails the request fails and the bank is never called
        await this.paymentRepo.Add(pending, cancellationToken);

        // a disconnected client can still stop the payment here; once the bank is called, cancelling
        // would leave its outcome unknown, so the call and the write that records it run to completion
        // (bounded by the bank client's resilience timeouts)
        cancellationToken.ThrowIfCancellationRequested();

        BankOutcome outcome = await this.bankClient.Authorize(pending.Id, request, CancellationToken.None);

        Payment completed = pending with
        {
            Status = outcome.Status,
            AuthorizationCode = outcome.AuthorizationCode,
            UpdatedAt = this.timeProvider.GetUtcNow()
        };

        try
        {
            await this.paymentRepo.Complete(completed, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // the bank has answered, so the merchant still gets the real outcome; the stored payment
            // stays Pending, and this log line carries what reconciliation needs to complete it
            this.LogCompleteFailed(exception, completed.Id, "Failed", completed.Status, completed.AuthorizationCode);
            return completed;
        }

        this.LogPaymentCompleted(completed.Id, "Completed", completed.Status);
        return completed;
    }

    public Task<Payment?> FindPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        return this.paymentRepo.Find(paymentId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogProcessPayment(Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, PaymentStatus={PaymentStatus}")]
    private partial void LogPaymentCompleted(Guid paymentId, string status, PaymentStatus paymentStatus);

    [LoggerMessage(Level = LogLevel.Critical, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, the bank's outcome was not stored, PaymentStatus={PaymentStatus}, AuthorizationCode={AuthorizationCode}")]
    private partial void LogCompleteFailed(Exception exception, Guid paymentId, string status, PaymentStatus paymentStatus, string? authorizationCode);
}
