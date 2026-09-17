using System;

namespace PaymentGatewayApi.Models;

public class PostPaymentResponse
{
    public string? PaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    public string? CardNumberLastFour { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? Currency { get; set; }
    public decimal Amount { get; set; }
}
