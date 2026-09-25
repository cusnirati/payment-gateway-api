namespace PaymentGatewayApi.Models;

// example json payload
// {
//   "cardnumber": "2222405343248873",
//   "ExpiryMonth": "11",
//   "ExpiryYear": "2027",
//   "currency": "GBP",
//   "amount": 100,
//   "cvv": "123"
// }

public class PaymentRequest
{
    public required string CardNumber { get; set; }
    public required string Cvv { get; set; }
    public required string Currency { get; set; }

    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    public long Amount { get; set; }
    public string? PaymentId { get; set; }
}
