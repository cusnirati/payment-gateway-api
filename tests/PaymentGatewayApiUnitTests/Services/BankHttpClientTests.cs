using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankHttpClientTests
{
    [Theory]
    [InlineData(4, 2027, "04/2027")]
    [InlineData(11, 2027, "11/2027")]
    public void ToBankRequest_FormatsExpiryAsTwoDigitMonthAndFourDigitYear(int month, int year, string expected)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();
        payment.ExpiryMonth = month;
        payment.ExpiryYear = year;

        BankRequest request = BankHttpClient.ToBankRequest(payment);

        Assert.Equal(expected, request.ExpiryDate);
    }

    [Fact]
    public void ToBankRequest_MapsPaymentFields()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest();

        BankRequest request = BankHttpClient.ToBankRequest(payment);

        Assert.Equal("2222405343248873", request.CardNumber);
        Assert.Equal("GBP", request.Currency);
        Assert.Equal(100, request.Amount);
        Assert.Equal("123", request.Cvv);
    }
}
