using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankHttpClientTests
{
    private readonly Mock<ILogger<BankHttpClient>> logger = new();
    private readonly PaymentRequest payment = TestPayments.Create();

    [Fact]
    public async Task PostPayment_SendsTheBankItsContract()
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}"""));

        await this.CreateClient(handler).PostPayment(this.payment);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://bank.test/payments"), request.RequestUri);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        JsonElement root = body.RootElement;
        Assert.Equal(
            new[] { "amount", "card_number", "currency", "cvv", "expiry_date", "paymentId" },
            root.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("2223000000000007", root.GetProperty("card_number").GetString());
        Assert.Equal("123", root.GetProperty("cvv").GetString());
        Assert.Equal("04/2030", root.GetProperty("expiry_date").GetString());
        Assert.Equal(100, root.GetProperty("amount").GetInt64());
        Assert.Equal("GBP", root.GetProperty("currency").GetString());
        Assert.Equal("1bfb4527-35b5-4a0c-84d1-c3227fd63b5d", root.GetProperty("paymentId").GetString());
    }

    [Theory]
    [InlineData(1, "01/2030")]
    [InlineData(12, "12/2030")]
    public async Task PostPayment_ExpiryDate_IsTwoDigitMonthSlashYear(int month, string expiryDate)
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, """{"authorized":false}"""));
        this.payment.ExpiryMonth = month;

        await this.CreateClient(handler).PostPayment(this.payment);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(expiryDate, body.RootElement.GetProperty("expiry_date").GetString());
    }

    [Fact]
    public async Task PostPayment_Authorized_ReturnsTheBankResponse()
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}"""));

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.True(response.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", response.AuthorizationCode);
        Assert.Null(response.Reason);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Fact]
    public async Task PostPayment_Declined_ReturnsTheBankResponse()
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, """{"authorized":false,"reason":"insufficient funds"}"""));

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.False(response.Authorized);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal("insufficient funds", response.Reason);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PostPayment_UnsuccessfulStatus_ReturnsNullAndLogsTheError(HttpStatusCode status)
    {
        StubHttpMessageHandler handler = new(_ => Json(status, "{}"));

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLoggedWith<BankHttpClient, HttpRequestException>(LogLevel.Error);
    }

    [Fact]
    public async Task PostPayment_BankUnreachable_ReturnsNullAndLogsTheError()
    {
        HttpRequestException exception = new("Connection refused");
        StubHttpMessageHandler handler = new(_ => throw exception);

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }

    [Fact]
    public async Task PostPayment_Timeout_ReturnsNullAndLogsTheError()
    {
        TaskCanceledException exception = new("The request was canceled due to the configured HttpClient.Timeout");
        StubHttpMessageHandler handler = new(_ => throw exception);

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }

    [Fact]
    public async Task PostPayment_NullBody_ReturnsNullAndLogsTheError()
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, "null"));

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData("""{"authorized":""")]
    [InlineData("not json")]
    [InlineData("""{"authorized":"yes"}""")]
    public async Task PostPayment_UnreadableBody_ReturnsNullAndLogsTheError(string body)
    {
        StubHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, body));

        BankResponse? response = await this.CreateClient(handler).PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLoggedWith<BankHttpClient, JsonException>(LogLevel.Error);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private BankHttpClient CreateClient(StubHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("http://bank.test/") };
        return new BankHttpClient(this.logger.Object, httpClient);
    }
}
