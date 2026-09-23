using System.Globalization;
using System.Text;

namespace PaymentGatewayApi.Models;

// example json payload
// {
//   "cardNumber": "2222405343248873",
//   "expiryMonth": 4,
//   "expiryYear": 2027,
//   "currency": "GBP",
//   "amount": 1050,
//   "cvv": "123"
// }

public sealed record PaymentRequest
{
    public required string CardNumber { get; init; }
    public required int ExpiryMonth { get; init; }
    public required int ExpiryYear { get; init; }

    // ISO 4217 code, one of Payments:SupportedCurrencies
    public required string Currency { get; init; }

    // in the currency's minor unit, 1050 is £10.50
    public required int Amount { get; init; }

    public required string Cvv { get; init; }

    // a record prints every property by default; keep the card number and CVV out of any
    // log line or exception message that formats the request
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("CardNumber = ****, ");
        builder.Append(CultureInfo.InvariantCulture, $"ExpiryMonth = {this.ExpiryMonth}, ExpiryYear = {this.ExpiryYear}, ");
        builder.Append(CultureInfo.InvariantCulture, $"Currency = {this.Currency}, Amount = {this.Amount}, Cvv = ***");
        return true;
    }
}
