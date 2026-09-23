using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankResponseProcessorTests
{
    private readonly BankResponseProcessor processor = new(NullLogger<BankResponseProcessor>.Instance);

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task ParseBankResponse_Authorized_MapsRequestOntoResponse()
    {
        PaymentRequest request = TestData.ValidPaymentRequest();
        using HttpResponseMessage response = JsonResponse(
            """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");

        PostPaymentResponse? result = await this.processor.ParseBankResponse(request, response);

        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal(TestData.PaymentId, result.PaymentId);
        Assert.Equal("8873", result.CardNumberLastFour);
        Assert.Equal(11, result.ExpiryMonth);
        Assert.Equal(2027, result.ExpiryYear);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal(100, result.Amount);
    }

    [Fact]
    public async Task ParseBankResponse_NotAuthorized_ReturnsDeclined()
    {
        PaymentRequest request = TestData.ValidPaymentRequest();
        using HttpResponseMessage response = JsonResponse("""{"authorized":false,"authorization_code":""}""");

        PostPaymentResponse? result = await this.processor.ParseBankResponse(request, response);

        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Declined, result.Status);
        Assert.Equal(TestData.PaymentId, result.PaymentId);
        Assert.Equal("8873", result.CardNumberLastFour);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task ParseBankResponse_UnusableBody_ReturnsNull(string body)
    {
        PaymentRequest request = TestData.ValidPaymentRequest();
        using HttpResponseMessage response = JsonResponse(body);

        PostPaymentResponse? result = await this.processor.ParseBankResponse(request, response);

        Assert.Null(result);
    }
}
