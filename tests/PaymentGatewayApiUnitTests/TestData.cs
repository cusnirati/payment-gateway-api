using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApiUnitTests;

public static class TestData
{
    public const string PaymentId = "16bd4749-d150-48d1-a3ce-257457d74d1f";

    public static PaymentRequest ValidPaymentRequest()
    {
        return new PaymentRequest
        {
            CardNumber = "2223000000000007",
            Cvv = "123",
            Currency = "GBP",
            ExpiryMonth = 11,
            ExpiryYear = 2027,
            Amount = 100,
            PaymentId = PaymentId
        };
    }
}
