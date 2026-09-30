using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

// example json payload
// {
//   "card_number": "2222405343248873",
//   "expiry_date": "04/2025",
//   "currency": "GBP",
//   "amount": 100,
//   "cvv": "123"
// }

public class BankRequest
{
    [JsonPropertyName("card_number")]
    public required string CardNumber { get; set; }

    [JsonPropertyName("expiry_date")]
    public required string ExpiryDate { get; set; }

    public required string Currency { get; set; }
    public long Amount { get; set; }
    public required string Cvv { get; set; }
    public string? PaymentId { get; set; }
}
