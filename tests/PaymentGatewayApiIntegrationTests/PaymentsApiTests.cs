using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

public class PaymentsApiTests
{
    private readonly GatewayFactory factory;
    private readonly HttpClient client;

    public PaymentsApiTests(GatewayFactory factory)
    {
        this.factory = factory;
        this.client = factory.CreateMerchantClient();
    }

    [Fact]
    public async Task Post_Authorized_CreatedAndStoredWithItsAuthorizationCode()
    {
        string card = Cards.Unique('3');

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonNode created = await response.ReadJson();
        string id = created["id"]!.GetValue<string>();
        Assert.Equal("Authorized", created["status"]!.GetValue<string>());
        Assert.Equal($"/api/payments/{id}", response.Headers.Location?.AbsolutePath);

        // the storage record, read straight from DynamoDB
        using IAmazonDynamoDB dynamo = this.factory.NewDynamoClient();
        GetItemResponse item = await dynamo.GetItemAsync(
            "Payments", new Dictionary<string, AttributeValue> { ["PaymentId"] = new(id) }, TestContext.Current.CancellationToken);
        Assert.Equal("Authorized", item.Item["Status"].S);
        Assert.Equal("merchant-a", item.Item["MerchantId"].S);
        Assert.Equal(card[^4..], item.Item["CardNumberLastFour"].S);
        Assert.True(Guid.TryParse(item.Item["AuthorizationCode"].S, out _));
        Assert.DoesNotContain(item.Item.Values, value => value.S == card);
    }

    [Fact]
    public async Task Post_SendsTheBankItsContract()
    {
        string card = Cards.Unique('3');

        await this.client.PostPayment(Cards.Payment(card, amount: 1, currency: "eur"));

        JsonNode sent = Assert.Single(this.factory.Bank.RequestsFor(card));
        Assert.Equal($"04/{DateTime.UtcNow.Year + 2}", sent["expiry_date"]!.GetValue<string>());
        Assert.Equal("EUR", sent["currency"]!.GetValue<string>());
        Assert.Equal(1, sent["amount"]!.GetValue<int>());
        Assert.Equal("123", sent["cvv"]!.GetValue<string>());
    }

    [Fact]
    public async Task Post_Declined_CreatedAndRetrievable()
    {
        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(Cards.Unique('2')));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        HttpResponseMessage stored = await this.client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal("Declined", (await stored.ReadJson())["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Post_Invalid_ValidationProblemAndTheBankIsNeverCalled()
    {
        string card = Cards.Unique('3');

        HttpResponseMessage response = await this.client.PostPayment(Cards.Payment(card, amount: 0, currency: "JPY"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonObject errors = (await response.ReadJson())["errors"]!.AsObject();
        Assert.True(errors.ContainsKey("amount"));
        Assert.True(errors.ContainsKey("currency"));
        Assert.Empty(this.factory.Bank.RequestsFor(card));
    }

    [Fact]
    public async Task Get_UnknownId_NotFoundProblem()
    {
        HttpResponseMessage response = await this.client.GetAsync($"api/payments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HealthReady_TablesReachable_Healthy()
    {
        using HttpClient anonymous = this.factory.CreateMerchantClient(apiKey: null);

        HttpResponseMessage response = await anonymous.GetAsync("health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
