using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    // ISO 4217 codes the gateway accepts
    [Required]
    [MinLength(1)]
    public string[] SupportedCurrencies { get; set; } = [];
}
