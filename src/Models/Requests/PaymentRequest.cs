using System;

namespace PaymentGatewayApi.Models.Requests;

// example json payload
// {
//   "cardnumber": "2223000000000007",
//   "ExpiryMonth": "11",
//   "ExpiryYear": "2027",
//   "currency": "GBP",
//   "amount": 100,
//   "cvv": "123",
//   "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d"
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

    // supplied by the merchant and used as the idempotency key; the merchant guarantees it is unique per payment
    public required Guid PaymentId { get; set; }
}
