using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Options;

namespace PaymentGatewayApi.Configuration;

public sealed class ApiKeyOptions : IValidatableObject
{
    public const string SectionName = "ApiKeys";

    [Required]
    [MinLength(1)]
    [ValidateEnumeratedItems]
    public MerchantApiKey[] Merchants { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // one key, one merchant: a shared hash would make the key authenticate as either
        if (this.Merchants.Select(merchant => merchant.KeySha256).Distinct().Count() != this.Merchants.Length)
        {
            yield return new ValidationResult("Each KeySha256 must be unique.", [nameof(this.Merchants)]);
        }
    }
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

// source-generated, and unlike ValidateDataAnnotations it validates each configured merchant too
[OptionsValidator]
public sealed partial class ApiKeyOptionsValidator : IValidateOptions<ApiKeyOptions>;
