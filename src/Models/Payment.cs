using System;

namespace PaymentGatewayApi.Models;

// the gateway's record of a payment; only the last four card digits are ever kept
public sealed record Payment
{
    public required Guid Id { get; init; }
    public required PaymentStatus Status { get; init; }
    public required string CardNumberLastFour { get; init; }
    public required int ExpiryMonth { get; init; }
    public required int ExpiryYear { get; init; }
    public required string Currency { get; init; }
    public required decimal Amount { get; init; }
    public string? AuthorizationCode { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
