using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Serilog.Events;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

// the failure modes that decide whether money moves correctly, through the production resilience pipeline
public class ResilienceTests
{
    private readonly GatewayFactory factory;
    private readonly HttpClient client;

    public ResilienceTests(GatewayFactory factory)
    {
        this.factory = factory;
        this.client = factory.CreateMerchantClient();
    }

    private async Task<JsonNode> AssertBadGateway(HttpResponseMessage response, string paymentStatus)
    {
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        JsonNode problem = await response.ReadJson();
        Assert.Equal(paymentStatus, problem["paymentStatus"]!.GetValue<string>());

        // the payment is on record with the same status
        HttpResponseMessage stored = await this.client.GetAsync(
            $"api/payments/{problem["paymentId"]!.GetValue<string>()}", TestContext.Current.CancellationToken);
        Assert.Equal(paymentStatus, (await stored.ReadJson())["status"]!.GetValue<string>());
        return problem;
    }

    [Fact]
    public async Task Post_BankUnavailable_CalledExactlyOnceAndFailed()
    {
        string card = Cards.Unique('0');

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card));

        await this.AssertBadGateway(response, "Failed");
        Assert.Single(this.factory.Bank.RequestsFor(card));
    }

    [Fact]
    public async Task Post_BankServerError_NotRetriedAndUnknown()
    {
        string card = Cards.Unique('3');
        this.factory.Bank.Answer(card, _ => Task.FromResult(StubBank.Json(HttpStatusCode.InternalServerError, "{}")));

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card));

        await this.AssertBadGateway(response, "Unknown");
        Assert.Single(this.factory.Bank.RequestsFor(card));
    }

    [Fact]
    public async Task Post_BankTimesOut_NotRetriedAndUnknown()
    {
        string card = Cards.Unique('3');
        this.factory.Bank.Answer(card, async cancellationToken =>
        {
            // longer than the 2s attempt timeout the factory configures
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return StubBank.Json(HttpStatusCode.OK, """{"authorized":true}""");
        });

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card));

        await this.AssertBadGateway(response, "Unknown");
        Assert.Single(this.factory.Bank.RequestsFor(card));
    }

    [Fact]
    public async Task Post_BankAnswerUnreadable_Unknown()
    {
        string card = Cards.Unique('3');
        this.factory.Bank.Answer(card, _ => Task.FromResult(StubBank.Json(HttpStatusCode.OK, "<html>")));

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card));

        await this.AssertBadGateway(response, "Unknown");
    }

    [Fact]
    public async Task Post_OutcomeWriteFails_MerchantGetsTheOutcomeAndTheRecordStaysPending()
    {
        await using WebApplicationFactory<Program> failing = this.factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentsRepository>(provider =>
                new FailingCompleteRepository(ActivatorUtilities.CreateInstance<PaymentsRepository>(provider)))));
        using HttpClient failingClient = failing.CreateMerchantClient();

        HttpResponseMessage response = await failingClient.PostPayment(Cards.Payment(Cards.Unique('3')));

        // the bank authorized it, so that is what the merchant hears
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Authorized", (await response.ReadJson())["status"]!.GetValue<string>());

        // the record says Pending, and a Critical log line carries the outcome for reconciliation
        HttpResponseMessage stored = await failingClient.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal("Pending", (await stored.ReadJson())["status"]!.GetValue<string>());
        Assert.Contains(this.factory.Logs.Events, logEvent =>
            logEvent.Level == LogEventLevel.Fatal && logEvent.Properties["AuthorizationCode"].ToString().Length > 2);
    }

    [Fact]
    public async Task Post_ClientDisconnectsDuringTheBankCall_TheOutcomeIsStillRecorded()
    {
        string card = Cards.Unique('3');
        string key = Guid.NewGuid().ToString();
        using SemaphoreSlim bankCalled = new(0);
        this.factory.Bank.Answer(card, async _ =>
        {
            bankCalled.Release();
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
            return StubBank.Json(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"code-1"}""");
        });

        using CancellationTokenSource disconnect = new();
        Task<HttpResponseMessage> abandoned = this.client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "api/payments")
            {
                Content = System.Net.Http.Json.JsonContent.Create(Cards.Payment(card)),
                Headers = { { "Idempotency-Key", key } }
            },
            disconnect.Token);
        await bankCalled.WaitAsync(TestContext.Current.CancellationToken);
        await disconnect.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        // replaying the key shows what the abandoned request produced, once the bank has answered
        HttpResponseMessage replay = await this.client.PostPayment(Cards.Payment(card), key);
        for (int attempt = 0; replay.StatusCode == HttpStatusCode.Conflict && attempt < 20; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            replay = await this.client.PostPayment(Cards.Payment(card), key);
        }

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("Authorized", (await replay.ReadJson())["status"]!.GetValue<string>());
        Assert.Single(this.factory.Bank.RequestsFor(card));
    }

    // the real repository, except that recording the bank's outcome always fails
    private sealed class FailingCompleteRepository(IPaymentsRepository inner) : IPaymentsRepository
    {
        public Task Add(Payment payment, CancellationToken cancellationToken)
        {
            return inner.Add(payment, cancellationToken);
        }

        public Task<bool> Add(Payment payment, IdempotencyRecord idempotency, CancellationToken cancellationToken)
        {
            return inner.Add(payment, idempotency, cancellationToken);
        }

        public Task<IdempotencyRecord?> FindIdempotencyRecord(string merchantId, string key, CancellationToken cancellationToken)
        {
            return inner.FindIdempotencyRecord(merchantId, key, cancellationToken);
        }

        public Task Complete(Payment payment, CancellationToken cancellationToken)
        {
            throw new AmazonDynamoDBException("DynamoDB is unavailable");
        }

        public Task<Payment?> Find(Guid paymentId, CancellationToken cancellationToken)
        {
            return inner.Find(paymentId, cancellationToken);
        }
    }
}
