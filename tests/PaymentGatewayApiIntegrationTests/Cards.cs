using System;
using System.Globalization;

namespace PaymentGatewayApiIntegrationTests;

public static class Cards
{
    // a 16 digit card number no other test uses, ending in the digit that steers the stub bank
    public static string Unique(char lastDigit)
    {
        return "4" + Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString(CultureInfo.InvariantCulture) + lastDigit;
    }

    public static object Payment(string cardNumber, int amount = 1050, string currency = "GBP")
    {
        return new { cardNumber, expiryMonth = 4, expiryYear = DateTime.UtcNow.Year + 2, currency, amount, cvv = "123" };
    }
}
