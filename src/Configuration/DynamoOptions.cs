using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class DynamoOptions
{
    public const string SectionName = "Dynamo";

    [Required]
    public string PaymentsTable { get; set; } = null!;

    [Required]
    public string IdempotencyKeysTable { get; set; } = null!;
}
