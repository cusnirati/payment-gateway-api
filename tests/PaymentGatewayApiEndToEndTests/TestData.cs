using System;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApiEndToEndTests;

public static class TestData
{
    // the bank simulator picks its response from the last card digit
    public const string AuthorizedCard = "2222405343248873";
    public const string DeclinedCard = "2222405343248872";
    public const string BankUnavailableCard = "2222405343248870";

    public static PaymentRequest ValidPaymentRequest(string cardNumber)
    {
        return new PaymentRequest
        {
            CardNumber = cardNumber,
            Cvv = "123",
            Currency = "GBP",
            ExpiryMonth = 11,
            ExpiryYear = DateTime.Now.Year + 1,
            Amount = 100
        };
    }
}
