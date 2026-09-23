using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentService
{
    // takes a validated request through the bank and returns the payment with the bank's outcome; with
    // an idempotency key already used by this merchant, returns what that key's first request produced
    Task<PaymentAttempt> ProcessPayment(
        string merchantId, PaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken);

    // null when the payment does not exist or belongs to another merchant; the caller cannot tell which
    Task<Payment?> FindPayment(string merchantId, Guid paymentId, CancellationToken cancellationToken);
}

public partial class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> logger;
    private readonly IBankClient bankClient;
    private readonly IPaymentsRepository paymentRepo;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan idempotencyKeyRetention;

    public PaymentService(
        ILogger<PaymentService> logger,
        IBankClient bankClient,
        IPaymentsRepository paymentRepo,
        TimeProvider timeProvider,
        IOptions<PaymentOptions> options)
    {
        this.logger = logger;
        this.bankClient = bankClient;
        this.paymentRepo = paymentRepo;
        this.timeProvider = timeProvider;
        this.idempotencyKeyRetention = options.Value.IdempotencyKeyRetention;
    }

    public async Task<PaymentAttempt> ProcessPayment(
        string merchantId, PaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = this.timeProvider.GetUtcNow();
        Payment pending = new()
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
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
        if (idempotencyKey is null)
        {
            await this.paymentRepo.Add(pending, cancellationToken);
        }
        else
        {
            IdempotencyRecord idempotency = new()
            {
                MerchantId = merchantId,
                Key = idempotencyKey,
                PaymentId = pending.Id,
                RequestFingerprint = IdempotencyRecord.Fingerprint(request),
                CreatedAt = now,
                ExpiresAt = now + this.idempotencyKeyRetention
            };

            // the key and the payment are written together, so of two concurrent requests with the
            // same key exactly one reaches the bank
            if (!await this.paymentRepo.Add(pending, idempotency, cancellationToken))
            {
                return await this.ReplayOrReject(merchantId, idempotency, cancellationToken);
            }
        }

        // a disconnected client can still stop the payment here; once the bank is called, cancelling
        // would leave its outcome unknown, so the call and the write that records it run to completion
        // (bounded by the bank client's resilience timeouts)
        cancellationToken.ThrowIfCancellationRequested();

        return new PaymentAttempt.Processed(await this.Authorize(pending, request));
    }

    public async Task<Payment?> FindPayment(string merchantId, Guid paymentId, CancellationToken cancellationToken)
    {
        Payment? payment = await this.paymentRepo.Find(paymentId, cancellationToken);

        // another merchant's payment is reported exactly like a missing one, so ids cannot be probed
        return payment?.MerchantId == merchantId ? payment : null;
    }

    private async Task<Payment> Authorize(Payment pending, PaymentRequest request)
    {
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

    private async Task<PaymentAttempt> ReplayOrReject(string merchantId, IdempotencyRecord attempted, CancellationToken cancellationToken)
    {
        // the write lost to an unexpired record, and records are only deleted once expired
        IdempotencyRecord existing = await this.paymentRepo.FindIdempotencyRecord(merchantId, attempted.Key, cancellationToken)
            ?? throw new InvalidOperationException("The idempotency record that blocked the payment could not be read.");

        if (existing.RequestFingerprint != attempted.RequestFingerprint)
        {
            this.LogIdempotencyKeyReused(existing.PaymentId, "Rejected");
            return new PaymentAttempt.KeyReused();
        }

        // written in the same transaction as the key, so it exists; Pending until the first request completes it
        Payment? payment = await this.paymentRepo.Find(existing.PaymentId, cancellationToken);
        if (payment is null || payment.Status == PaymentStatus.Pending)
        {
            this.LogIdempotentReplay(existing.PaymentId, "InProgress");
            return new PaymentAttempt.InProgress(existing.PaymentId);
        }

        this.LogIdempotentReplay(payment.Id, "Replayed");
        return new PaymentAttempt.Replayed(payment);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}")]
    private partial void LogProcessPayment(Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, PaymentStatus={PaymentStatus}")]
    private partial void LogPaymentCompleted(Guid paymentId, string status, PaymentStatus paymentStatus);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, a repeated Idempotency-Key")]
    private partial void LogIdempotentReplay(Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, the Idempotency-Key was used for a different request")]
    private partial void LogIdempotencyKeyReused(Guid paymentId, string status);

    [LoggerMessage(Level = LogLevel.Critical, Message = "ProcessPayment, PaymentId={PaymentId}, Status={Status}, the bank's outcome was not stored, PaymentStatus={PaymentStatus}, AuthorizationCode={AuthorizationCode}")]
    private partial void LogCompleteFailed(Exception exception, Guid paymentId, string status, PaymentStatus paymentStatus, string? authorizationCode);
}
