using System;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApiUnitTests;

public static class TestData
{
    public static readonly Guid PaymentId = Guid.Parse("16bd4749-d150-48d1-a3ce-257457d74d1f");
    public static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    public static PaymentRequest ValidPaymentRequest()
    {
        return new PaymentRequest
        {
            CardNumber = "2222405343248873",
            Cvv = "123",
            Currency = "GBP",
            ExpiryMonth = 11,
            ExpiryYear = 2027,
            Amount = 100
        };
    }

    public static Payment Payment(PaymentStatus status)
    {
        return new Payment
        {
            Id = PaymentId,
            Status = status,
            CardNumberLastFour = "8873",
            ExpiryMonth = 11,
            ExpiryYear = 2027,
            Currency = "GBP",
            Amount = 100,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }
}
