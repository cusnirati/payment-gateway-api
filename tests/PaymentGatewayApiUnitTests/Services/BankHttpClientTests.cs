using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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
    private readonly StubBankHandler bank = new();
    private readonly BankHttpClient client;
    private readonly PaymentRequest payment = TestPayments.Create();

    public BankHttpClientTests()
    {
        HttpClient httpClient = new(this.bank) { BaseAddress = new Uri("http://bank.test/") };
        this.client = new BankHttpClient(this.logger.Object, httpClient);
    }

    [Fact]
    public async Task PostPayment_SendsTheBankItsContract()
    {
        this.bank.Respond(HttpStatusCode.OK, """{ "authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad" }""");

        await this.client.PostPayment(this.payment);

        Assert.Single(this.bank.Requests);
        Assert.Equal(HttpMethod.Post, this.bank.Requests[0].Method);
        Assert.Equal(new Uri("http://bank.test/payments"), this.bank.Requests[0].RequestUri);
        using JsonDocument body = JsonDocument.Parse(this.bank.RequestBodies[0]);
        JsonElement request = body.RootElement;
        Assert.Equal("2223000000000007", request.GetProperty("card_number").GetString());
        Assert.Equal("123", request.GetProperty("cvv").GetString());
        Assert.Equal("04/2030", request.GetProperty("expiry_date").GetString());
        Assert.Equal(100, request.GetProperty("amount").GetInt64());
        Assert.Equal("GBP", request.GetProperty("currency").GetString());
        Assert.Equal(this.payment.PaymentId, request.GetProperty("paymentId").GetString());
    }

    [Fact]
    public async Task PostPayment_SingleDigitMonth_IsSentZeroPadded()
    {
        this.payment.ExpiryMonth = 1;
        this.bank.Respond(HttpStatusCode.OK, """{ "authorized": false }""");

        await this.client.PostPayment(this.payment);

        using JsonDocument body = JsonDocument.Parse(this.bank.RequestBodies[0]);
        Assert.Equal("01/2030", body.RootElement.GetProperty("expiry_date").GetString());
    }

    [Fact]
    public async Task PostPayment_Authorized_ReturnsTheBankResponse()
    {
        this.bank.Respond(HttpStatusCode.OK, """{ "authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad" }""");

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.True(response.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", response.AuthorizationCode);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Fact]
    public async Task PostPayment_Declined_ReturnsTheBankResponse()
    {
        this.bank.Respond(HttpStatusCode.OK, """{ "authorized": false, "authorization_code": "", "reason": "insufficient funds" }""");

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.False(response.Authorized);
        Assert.Equal("insufficient funds", response.Reason);
        this.logger.VerifyNotLogged(LogLevel.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PostPayment_UnsuccessfulStatus_ReturnsNullAndLogsTheError(HttpStatusCode status)
    {
        this.bank.Respond(status, """{ "error_message": "failed" }""");

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.Null(response);
        this.logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostPayment_BankUnreachable_ReturnsNullAndLogsTheError()
    {
        HttpRequestException exception = new("Connection refused");
        this.bank.Throw(exception);

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }

    [Fact]
    public async Task PostPayment_ResponseNotJson_ReturnsNullAndLogsTheError()
    {
        this.bank.Respond(HttpStatusCode.OK, "<html>maintenance</html>");

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.Null(response);
        this.logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<JsonException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostPayment_NullJsonBody_ReturnsNullAndLogsTheError()
    {
        this.bank.Respond(HttpStatusCode.OK, "null");

        BankResponse? response = await this.client.PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error);
    }
}

// answers every request with the configured response, or throws the configured exception, and records what it was sent
public class StubBankHandler : HttpMessageHandler
{
    private HttpStatusCode status = HttpStatusCode.OK;
    private string body = "{}";
    private Exception? exception;

    public List<HttpRequestMessage> Requests { get; } = new();

    // the request content is disposed with the request, so its body is read while the request is in flight
    public List<string> RequestBodies { get; } = new();

    public void Respond(HttpStatusCode status, string body)
    {
        this.status = status;
        this.body = body;
        this.exception = null;
    }

    public void Throw(Exception exception)
    {
        this.exception = exception;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.Requests.Add(request);
        this.RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());

        if (this.exception is not null)
        {
            throw this.exception;
        }

        return new HttpResponseMessage(this.status) { Content = new StringContent(this.body, Encoding.UTF8, "application/json") };
    }
}
