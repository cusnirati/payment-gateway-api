using System.Collections.Generic;

namespace PaymentGatewayApi.Models;

public class MerchantResponse
{
    public string? PaymentId { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public List<ValidationError?>? ValidationErrors { get; set; }
}