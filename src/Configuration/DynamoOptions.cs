using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class DynamoOptions
{
    public const string SectionName = "Dynamo";

    [Required]
    public string PaymentsTable { get; set; } = null!;
}
