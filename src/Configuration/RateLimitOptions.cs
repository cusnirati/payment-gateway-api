using System;
using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

// payment POSTs each merchant may send per window, before answering 429
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; }
}
