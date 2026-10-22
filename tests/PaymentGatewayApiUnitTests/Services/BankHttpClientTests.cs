using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Polly.Timeout;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class BankHttpClientTests : IDisposable
{
    private readonly Mock<ILogger<BankHttpClient>> logger = new();
    private readonly StubHttpMessageHandler handler = new();
    // one PostPayment per test: BankHttpClient sets HttpClient.BaseAddress, which throws once a request was sent
    private readonly HttpClient httpClient;
    private readonly PaymentRequest payment = TestPayments.Create();

    public BankHttpClientTests()
    {
        this.httpClient = new HttpClient(this.handler);
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    [Fact]
    public async Task PostPayment_PostsToThePaymentsEndpointOfTheConfiguredBank()
    {
        this.handler.Respond = _ => JsonResponse(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"code"}""");

        await this.CreateClient("http://bank.test").PostPayment(this.payment);

        HttpRequestMessage request = Assert.Single(this.handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://bank.test/payments"), request.RequestUri);
    }

    [Fact]
    public async Task PostPayment_SendsTheBankContract()
    {
        this.handler.Respond = _ => JsonResponse(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"code"}""");
        this.payment.ExpiryMonth = 4;
        this.payment.ExpiryYear = 2027;

        await this.CreateClient("http://bank.test").PostPayment(this.payment);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(this.handler.RequestBodies));
        JsonElement root = body.RootElement;
        Assert.Equal(
            new[] { "amount", "card_number", "currency", "cvv", "expiry_date", "paymentId" },
            root.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(this.payment.CardNumber, root.GetProperty("card_number").GetString());
        Assert.Equal(this.payment.Cvv, root.GetProperty("cvv").GetString());
        Assert.Equal("04/2027", root.GetProperty("expiry_date").GetString());
        Assert.Equal(this.payment.Amount, root.GetProperty("amount").GetInt64());
        Assert.Equal(this.payment.Currency, root.GetProperty("currency").GetString());
        Assert.Equal(this.payment.PaymentId, root.GetProperty("paymentId").GetString());
    }

    [Fact]
    public async Task PostPayment_BankAuthorizes_ReturnsTheAuthorizationCode()
    {
        this.handler.Respond = _ => JsonResponse(
            HttpStatusCode.OK, """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.True(response.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", response.AuthorizationCode);
        Assert.Null(response.Reason);
    }

    [Fact]
    public async Task PostPayment_BankDeclines_ReturnsTheReason()
    {
        this.handler.Respond = _ => JsonResponse(HttpStatusCode.OK, """{"authorized":false,"reason":"insufficient funds"}""");

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.NotNull(response);
        Assert.False(response.Authorized);
        Assert.Null(response.AuthorizationCode);
        Assert.Equal("insufficient funds", response.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PostPayment_BankAnswersWithAnErrorStatus_ReturnsNull(HttpStatusCode status)
    {
        this.handler.Respond = _ => JsonResponse(status, "{}");

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.Null(response);
    }

    [Fact]
    public async Task PostPayment_BankUnreachable_ReturnsNullAndLogsTheError()
    {
        HttpRequestException exception = new("Connection refused");
        this.handler.Respond = _ => throw exception;

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.Null(response);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }

    [Fact]
    public async Task PostPayment_BankTimesOut_ReturnsNull()
    {
        this.handler.Respond = _ => throw new TimeoutRejectedException();

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.Null(response);
    }

    [Fact]
    public async Task PostPayment_BankAnswersWithInvalidJson_ReturnsNull()
    {
        this.handler.Respond = _ => JsonResponse(HttpStatusCode.OK, "not json");

        BankResponse? response = await this.CreateClient("http://bank.test").PostPayment(this.payment);

        Assert.Null(response);
    }

    [Fact]
    public async Task PostPayment_NoBankAddressConfigured_ReturnsNullWithoutCallingTheBank()
    {
        BankResponse? response = await this.CreateClient(bankBaseAddress: null).PostPayment(this.payment);

        Assert.Null(response);
        Assert.Empty(this.handler.Requests);
    }

    private BankHttpClient CreateClient(string? bankBaseAddress)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SimulatorBankBaseAddress"] = bankBaseAddress })
            .Build();
        return new BankHttpClient(this.logger.Object, config, this.httpClient);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

// answers every request with Respond and keeps what was sent
public class StubHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.Requests.Add(request);
        this.RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
        return this.Respond(request);
    }
}
