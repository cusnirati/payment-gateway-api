using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankClientTests
{
    private static (BankClient Client, StubHttpMessageHandler Handler) NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        StubHttpMessageHandler handler = new(respond);
        HttpClient http = new(handler) { BaseAddress = new Uri("http://bank.test/") };

        return (new BankClient(NullLogger<BankClient>.Instance, http), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Task<BankOutcome> Authorize(BankClient client)
    {
        return client.Authorize(TestData.PaymentId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(4, 2027, "04/2027")]
    [InlineData(11, 2027, "11/2027")]
    public void ToBankRequest_FormatsExpiryAsTwoDigitMonthAndFourDigitYear(int month, int year, string expected)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest() with { ExpiryMonth = month, ExpiryYear = year };

        BankRequest request = BankClient.ToBankRequest(payment);

        Assert.Equal(expected, request.ExpiryDate);
    }

    [Fact]
    public async Task Authorize_PostsTheBankContractToPayments()
    {
        (BankClient client, StubHttpMessageHandler handler) = NewClient(_ => Json(HttpStatusCode.OK, """{"authorized":false}"""));

        await Authorize(client);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://bank.test/payments"), request.RequestUri);

        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("2222405343248873", body.RootElement.GetProperty("card_number").GetString());
        Assert.Equal("11/2027", body.RootElement.GetProperty("expiry_date").GetString());
        Assert.Equal("GBP", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(1050, body.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("123", body.RootElement.GetProperty("cvv").GetString());
    }

    [Fact]
    public async Task Authorize_LowerCaseCurrency_IsSentUpperCase()
    {
        (BankClient client, StubHttpMessageHandler handler) = NewClient(_ => Json(HttpStatusCode.OK, """{"authorized":false}"""));

        await client.Authorize(
            TestData.PaymentId, TestData.ValidPaymentRequest() with { Currency = "gbp" }, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("GBP", body.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Authorize_BankAuthorizes_ReturnsAuthorizedWithCode()
    {
        (BankClient client, _) = NewClient(_ => Json(
            HttpStatusCode.OK, """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}"""));

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Authorized, outcome.Status);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", outcome.AuthorizationCode);
    }

    [Fact]
    public async Task Authorize_BankDeclines_ReturnsDeclined()
    {
        (BankClient client, _) = NewClient(_ => Json(HttpStatusCode.OK, """{"authorized":false,"authorization_code":""}"""));

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Declined, outcome.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PaymentStatus.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PaymentStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, PaymentStatus.Unknown)]
    [InlineData(HttpStatusCode.GatewayTimeout, PaymentStatus.Unknown)]
    public async Task Authorize_BankReturnsError_MapsWhetherTheCardCouldHaveBeenCharged(HttpStatusCode statusCode, PaymentStatus expected)
    {
        (BankClient client, _) = NewClient(_ => Json(statusCode, "{}"));

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(expected, outcome.Status);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task Authorize_UnreadableBody_ReturnsUnknown(string body)
    {
        (BankClient client, _) = NewClient(_ => Json(HttpStatusCode.OK, body));

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Unknown, outcome.Status);
    }

    [Fact]
    public async Task Authorize_CircuitOpen_ReturnsFailed()
    {
        (BankClient client, _) = NewClient(_ => throw new BrokenCircuitException());

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Failed, outcome.Status);
    }

    [Fact]
    public async Task Authorize_TimedOut_ReturnsUnknown()
    {
        (BankClient client, _) = NewClient(_ => throw new TimeoutRejectedException());

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Unknown, outcome.Status);
    }

    [Fact]
    public async Task Authorize_ConnectionDropped_ReturnsUnknown()
    {
        (BankClient client, _) = NewClient(_ => throw new HttpRequestException("connection reset"));

        BankOutcome outcome = await Authorize(client);

        Assert.Equal(PaymentStatus.Unknown, outcome.Status);
    }
}
