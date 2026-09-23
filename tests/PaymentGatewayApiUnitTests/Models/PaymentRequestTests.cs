using PaymentGatewayApi.Models;
using Xunit;

namespace PaymentGatewayApiUnitTests.Models;

public class PaymentRequestTests
{
    [Fact]
    public void ToString_LeavesOutCardNumberAndCvv()
    {
        PaymentRequest request = TestData.ValidPaymentRequest() with { Cvv = "987" };

        string printed = request.ToString();

        Assert.DoesNotContain("2222405343248873", printed);
        Assert.DoesNotContain("987", printed);
        Assert.Contains("GBP", printed);
    }
}
