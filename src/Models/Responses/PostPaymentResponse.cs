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

    // the authorization code and timestamps stay internal to the gateway
    public static PostPaymentResponse From(Payment payment)
    {
        return new PostPaymentResponse
        {
            PaymentId = payment.Id.ToString(),
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}
