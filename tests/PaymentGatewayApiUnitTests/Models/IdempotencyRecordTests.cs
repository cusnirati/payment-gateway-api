using PaymentGatewayApi.Models;
using Xunit;

namespace PaymentGatewayApiUnitTests.Models;

public class IdempotencyRecordTests
{
    [Theory]
    [InlineData("order-42")]
    [InlineData("16bd4749-d150-48d1-a3ce-257457d74d1f")]
    [InlineData("a:b/c_d.e")]
    public void IsValidKey_VisibleAscii_IsTrue(string key)
    {
        Assert.True(IdempotencyRecord.IsValidKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("naïve")]
    public void IsValidKey_EmptyOrNonVisibleAscii_IsFalse(string key)
    {
        Assert.False(IdempotencyRecord.IsValidKey(key));
    }

    [Fact]
    public void IsValidKey_LongerThan255_IsFalse()
    {
        Assert.False(IdempotencyRecord.IsValidKey(new string('a', 256)));
    }

    [Fact]
    public void Fingerprint_SameRequest_IsStable()
    {
        Assert.Equal(
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest()),
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest() with { Currency = "gbp" }));
    }

    [Fact]
    public void Fingerprint_DifferentAmount_Differs()
    {
        Assert.NotEqual(
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest()),
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest() with { Amount = 1 }));
    }

    [Fact]
    public void Fingerprint_LeavesOutCvvAndAllButTheLastFourDigits()
    {
        // PCI DSS: the CVV may not be kept in any form, the full card number only under a keyed hash
        Assert.Equal(
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest()),
            IdempotencyRecord.Fingerprint(TestData.ValidPaymentRequest() with { CardNumber = "4111111111118873", Cvv = "999" }));
    }
}
