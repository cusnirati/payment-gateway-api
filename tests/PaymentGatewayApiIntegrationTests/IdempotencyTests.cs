using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

public class IdempotencyTests
{
    private readonly GatewayFactory factory;
    private readonly HttpClient client;

    public IdempotencyTests(GatewayFactory factory)
    {
        this.factory = factory;
        this.client = factory.CreateMerchantClient();
    }

    [Fact]
    public async Task Post_ConcurrentRequestsWithOneKey_TheBankIsCalledOnce()
    {
        string card = Cards.Unique('3');
        string key = Guid.NewGuid().ToString();
        this.factory.Bank.Answer(card, async _ =>
        {
            // slow enough that every request arrives while the first is still at the bank
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
            return StubBank.Json(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"code-1"}""");
        });

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => this.client.PostPayment(Cards.Payment(card), key)));

        Assert.Single(this.factory.Bank.RequestsFor(card));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created && !response.Headers.Contains("Idempotent-Replayed"));
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
    }

    [Fact]
    public async Task Post_RepeatedKey_ReplaysWithoutCallingTheBankAgain()
    {
        string card = Cards.Unique('3');
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage first = await this.client.PostPayment(Cards.Payment(card), key);
        HttpResponseMessage second = await this.client.PostPayment(Cards.Payment(card), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Single(this.factory.Bank.RequestsFor(card));
    }
}
