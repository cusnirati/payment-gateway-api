using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApiUnitTests;

// a valid payment request, each test changes only the fields it is about
public static class TestPayments
{
    public static PaymentRequest Create()
    {
        return new PaymentRequest
        {
            PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d",
            CardNumber = "2223000000000007",
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Cvv = "123",
            Amount = 100,
            Currency = "GBP"
        };
    }
}
