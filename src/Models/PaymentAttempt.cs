using System;

namespace PaymentGatewayApi.Models;

// what became of one POST: a payment taken through the bank, or, for a repeated Idempotency-Key,
// what the first request with that key produced
public abstract record PaymentAttempt
{
    private PaymentAttempt()
    {
    }

    public sealed record Processed(Payment Payment) : PaymentAttempt;

    // the key was used before with the same request: this is that request's payment, unchanged
    public sealed record Replayed(Payment Payment) : PaymentAttempt;

    // the key's first request has not finished yet, the caller should retry later
    public sealed record InProgress(Guid PaymentId) : PaymentAttempt;

    // the key was used before for a different request
    public sealed record KeyReused : PaymentAttempt;
}
