namespace PaymentGatewayApi.Models.Requests;

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

    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    // 100 for 1 GBP
    public long Amount { get; set; }
    public required string Currency { get; set; }

    public string? PaymentId { get; set; }
}
