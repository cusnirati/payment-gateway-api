using System;
using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    // ISO 4217 codes the gateway accepts
    [Required]
    [MinLength(1)]
    public string[] SupportedCurrencies { get; set; } = [];

    // how long an Idempotency-Key replays its payment before it can be used again
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan IdempotencyKeyRetention { get; set; }
}
