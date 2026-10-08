namespace PaymentGatewayApi.Models.Responses;

public class MerchantResponse
{
    public string? PaymentId { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
}