using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    [Required]
    [MinLength(1)]
    public MerchantApiKey[] Merchants { get; set; } = [];
}

// only the SHA-256 of a key is configured, so the settings (and anyone who can read them) never hold a usable key
public sealed class MerchantApiKey
{
    [Required]
    public string MerchantId { get; set; } = null!;

    // lower-case hex, e.g. printf %s "$KEY" | sha256sum
    [Required]
    [RegularExpression("^[0-9a-f]{64}$")]
    public string KeySha256 { get; set; } = null!;
}
