using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models.Requests;

// example json payload
// {
//   "card_number": "2223000000000007",
//   "cvv": "123",
//   "expiry_date": "04/2025",
//   "amount": 100,
//   "currency": "GBP",
//   "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d",
// }

public class BankRequest
{
    [JsonPropertyName("card_number")]
    public required string CardNumber { get; set; }
    public required string Cvv { get; set; }

    [JsonPropertyName("expiry_date")]
    public required string ExpiryDate { get; set; }

    public long Amount { get; set; }
    public required string Currency { get; set; }

    public string? PaymentId { get; set; }
}
