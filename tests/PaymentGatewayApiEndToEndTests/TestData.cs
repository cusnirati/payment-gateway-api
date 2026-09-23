using System;

namespace PaymentGatewayApiEndToEndTests;

// the request as a merchant would send it, independent of the api's own model
public sealed record PaymentBody(string CardNumber, int ExpiryMonth, int ExpiryYear, string Currency, int Amount, string Cvv);

public static class TestData
{
    // the bank simulator picks its response from the last card digit
    public const string AuthorizedCard = "2222405343248873";
    public const string DeclinedCard = "2222405343248872";
    public const string BankUnavailableCard = "2222405343248870";

    public static PaymentBody ValidPayment(string cardNumber)
    {
        return new PaymentBody(cardNumber, ExpiryMonth: 11, ExpiryYear: DateTime.UtcNow.Year + 1, Currency: "GBP", Amount: 1050, Cvv: "123");
    }
}
