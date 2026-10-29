namespace PaymentGatewayApi.Models;

public class PaymentDb
{
    public string? CardNumberLastFour { get; set; }

    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    public long Amount { get; set; }
    public string? Currency { get; set; }

    public string? PaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    public string? BankAuthorizationCode { get; set; }
    public string? BankReason { get; set; }
}
