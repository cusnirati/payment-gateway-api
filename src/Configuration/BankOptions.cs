using System;
using System.ComponentModel.DataAnnotations;

namespace PaymentGatewayApi.Configuration;

public sealed class BankOptions
{
    public const string SectionName = "Bank";

    // the acquiring bank's base address, payments are POSTed to "payments" under it
    [Required]
    public Uri BaseUrl { get; set; } = null!;
}
