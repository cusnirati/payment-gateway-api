using System;
using System.Text.Json.Nodes;

namespace PaymentGatewayApiEndToEndTests;

// valid payment requests as a merchant posts them
public static class TestPayments
{
    // the bank simulator answers by the card number's last digit, see src/Simulators/wiremock/mappings
    public const string AuthorizedCard = "2223000000000007"; // odd
    public const string DeclinedCard = "5123450000000008"; // even
    public const string UnavailableCard = "4318722273695740"; // zero, the simulator answers 503

    public static JsonObject Create(string cardNumber)
    {
        return new JsonObject
        {
            ["cardNumber"] = cardNumber,
            ["expiryMonth"] = 4,
            ["expiryYear"] = DateTime.UtcNow.Year + 1,
            ["amount"] = 100,
            ["currency"] = "GBP",
            ["cvv"] = "123"
        };
    }
}
