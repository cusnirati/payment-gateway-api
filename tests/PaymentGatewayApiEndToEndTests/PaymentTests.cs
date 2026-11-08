using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class PaymentTests
{
    private readonly ApiFixture fixture;

    public PaymentTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(TestPayments.AuthorizedCard, "Authorized")]
    [InlineData(TestPayments.DeclinedCard, "Declined")]
    [InlineData(TestPayments.UnavailableCard, "Unavailable")]
    public async Task PostPayment_ReturnsTheBankOutcome_AndGetPaymentReturnsTheSame(string cardNumber, string status)
    {
        JsonObject request = TestPayments.Create(cardNumber);
        string paymentId = request["paymentId"]!.GetValue<string>();

        using HttpResponseMessage post = await this.fixture.Api.PostAsJsonAsync("api/payments", request);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal($"/api/payments/{paymentId}", post.Headers.Location?.AbsolutePath);
        JsonElement created = await post.Content.ReadFromJsonAsync<JsonElement>();
        AssertMerchantResponse(created, paymentId, status, 100, "GBP");

        using HttpResponseMessage get = await this.fixture.Api.GetAsync($"api/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        JsonElement retrieved = await get.Content.ReadFromJsonAsync<JsonElement>();
        AssertMerchantResponse(retrieved, paymentId, status, 100, "GBP");
    }

    [Fact]
    public async Task PostPayment_Authorized_StoresTheAuthorizationCodeButNeverTheFullCardNumberOrCvv()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        string paymentId = await this.PostPayment(TestPayments.Create(TestPayments.AuthorizedCard));
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Dictionary<string, AttributeValue>? item = await this.fixture.GetStoredItem(paymentId);
        Assert.NotNull(item);
        Assert.Equal(new[] { "CreatedAt", "PaymentId", "PaymentRaw", "Status" }, item.Keys.Order());
        Assert.InRange(DateTimeOffset.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture), before, after);
        Assert.Equal("Authorized", item["Status"].S);

        string raw = item["PaymentRaw"].S;
        Assert.DoesNotContain(TestPayments.AuthorizedCard, raw);
        using JsonDocument stored = JsonDocument.Parse(raw);
        JsonElement payment = stored.RootElement;
        Assert.Equal(
            new[] { "Amount", "BankAuthorizationCode", "BankReason", "CardNumberLastFour", "Currency", "ExpiryMonth", "ExpiryYear", "PaymentId", "Status" },
            payment.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("0007", payment.GetProperty("CardNumberLastFour").GetString());
        Assert.Equal(paymentId, payment.GetProperty("PaymentId").GetString());
        Assert.Equal("Authorized", payment.GetProperty("Status").GetString());
        Assert.True(Guid.TryParse(payment.GetProperty("BankAuthorizationCode").GetString(), out _));
        Assert.Equal(100, payment.GetProperty("Amount").GetInt64());
        Assert.Equal("GBP", payment.GetProperty("Currency").GetString());
    }

    [Fact]
    public async Task PostPayment_Declined_StoresTheBankReason()
    {
        string paymentId = await this.PostPayment(TestPayments.Create(TestPayments.DeclinedCard));

        Dictionary<string, AttributeValue>? item = await this.fixture.GetStoredItem(paymentId);
        Assert.NotNull(item);
        Assert.Equal("Declined", item["Status"].S);
        using JsonDocument stored = JsonDocument.Parse(item["PaymentRaw"].S);
        Assert.Equal("Declined", stored.RootElement.GetProperty("Status").GetString());
        Assert.Equal("insufficient funds", stored.RootElement.GetProperty("BankReason").GetString());
    }

    [Fact]
    public async Task PostPayment_BankUnavailable_RetriesTheBankOnce()
    {
        string paymentId = await this.PostPayment(TestPayments.Create(TestPayments.UnavailableCard));

        List<BankSimulatorRequest> bankRequests = await this.fixture.FindBankRequests("$.paymentId", paymentId);

        Assert.Equal(2, bankRequests.Count);
    }

    [Fact]
    public async Task PostPayment_SendsTheBankItsContractOnce()
    {
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        request["currency"] = "gbp";
        int expiryYear = request["expiryYear"]!.GetValue<int>();

        string paymentId = await this.PostPayment(request);

        BankSimulatorRequest bankRequest = Assert.Single(await this.fixture.FindBankRequests("$.paymentId", paymentId));
        Assert.Equal(TestPayments.AuthorizedCard, bankRequest.Body.GetProperty("card_number").GetString());
        Assert.Equal("123", bankRequest.Body.GetProperty("cvv").GetString());
        Assert.Equal($"04/{expiryYear}", bankRequest.Body.GetProperty("expiry_date").GetString());
        Assert.Equal(100, bankRequest.Body.GetProperty("amount").GetInt64());
        Assert.Equal("GBP", bankRequest.Body.GetProperty("currency").GetString());
        Assert.Equal(paymentId, bankRequest.Body.GetProperty("paymentId").GetString());
    }

    [Fact]
    public async Task PostPayment_PaymentIdFromTheMerchant_IsThePaymentIdThroughout()
    {
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        string merchantPaymentId = request["paymentId"]!.GetValue<string>();

        string paymentId = await this.PostPayment(request);

        Assert.Equal(merchantPaymentId, paymentId);
        Assert.Single(await this.fixture.FindBankRequests("$.paymentId", merchantPaymentId));
        Assert.NotNull(await this.fixture.GetStoredItem(merchantPaymentId));
    }

    [Theory]
    [InlineData(TestPayments.AuthorizedCard, "Authorized")]
    [InlineData(TestPayments.DeclinedCard, "Declined")]
    public async Task PostPayment_SamePaymentIdAgain_ReplaysTheStoredOutcomeWithoutCallingTheBankAgain(string cardNumber, string status)
    {
        JsonObject request = TestPayments.Create(cardNumber);
        string paymentId = await this.PostPayment(request);
        Dictionary<string, AttributeValue>? first = await this.fixture.GetStoredItem(paymentId);

        using HttpResponseMessage replay = await this.fixture.Api.PostAsJsonAsync("api/payments", request);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal($"/api/payments/{paymentId}", replay.Headers.Location?.AbsolutePath);
        AssertMerchantResponse(await replay.Content.ReadFromJsonAsync<JsonElement>(), paymentId, status, 100, "GBP");
        Assert.Single(await this.fixture.FindBankRequests("$.paymentId", paymentId));
        Dictionary<string, AttributeValue>? second = await this.fixture.GetStoredItem(paymentId);
        Assert.Equal(first!["CreatedAt"].S, second!["CreatedAt"].S);
        Assert.Equal(first["PaymentRaw"].S, second["PaymentRaw"].S);
    }

    [Fact]
    public async Task PostPayment_SamePaymentIdWithADifferentBody_ReturnsTheOriginalPayment()
    {
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        string paymentId = await this.PostPayment(request);
        JsonObject changed = TestPayments.Create(TestPayments.DeclinedCard);
        changed["paymentId"] = paymentId;
        changed["amount"] = 999;
        changed["currency"] = "EUR";

        using HttpResponseMessage replay = await this.fixture.Api.PostAsJsonAsync("api/payments", changed);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        AssertMerchantResponse(await replay.Content.ReadFromJsonAsync<JsonElement>(), paymentId, "Authorized", 100, "GBP");
        Assert.Single(await this.fixture.FindBankRequests("$.paymentId", paymentId));
    }

    [Fact]
    public async Task PostPayment_SamePaymentIdAfterUnavailable_CallsTheBankAgainAndStoresTheNewOutcome()
    {
        JsonObject request = TestPayments.Create(TestPayments.UnavailableCard);
        string paymentId = await this.PostPayment(request);
        // the simulator answers by card number, a different one stands in for the bank having recovered
        request["cardNumber"] = TestPayments.AuthorizedCard;

        using HttpResponseMessage retry = await this.fixture.Api.PostAsJsonAsync("api/payments", request);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        AssertMerchantResponse(await retry.Content.ReadFromJsonAsync<JsonElement>(), paymentId, "Authorized", 100, "GBP");
        // 2 attempts for the first post, 1 for the retry
        Assert.Equal(3, (await this.fixture.FindBankRequests("$.paymentId", paymentId)).Count);
        Dictionary<string, AttributeValue>? item = await this.fixture.GetStoredItem(paymentId);
        Assert.NotNull(item);
        Assert.Equal("Authorized", item["Status"].S);

        using HttpResponseMessage get = await this.fixture.Api.GetAsync($"api/payments/{paymentId}");
        AssertMerchantResponse(await get.Content.ReadFromJsonAsync<JsonElement>(), paymentId, "Authorized", 100, "GBP");
    }

    [Fact]
    public async Task PostPayment_ReadmePayload_IsAccepted()
    {
        // as in the README and the Bruno collection: property names in any case, numbers as strings
        string paymentId = Guid.NewGuid().ToString();
        string body = $$"""
            {
              "cardnumber": "{{TestPayments.AuthorizedCard}}",
              "ExpiryMonth": "11",
              "ExpiryYear": "{{DateTime.UtcNow.Year + 1}}",
              "currency": "GBP",
              "amount": 100,
              "cvv": "123",
              "paymentId": "{{paymentId}}"
            }
            """;

        using HttpResponseMessage response = await this.fixture.Api.PostAsync("api/payments", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertMerchantResponse(payment, paymentId, "Authorized", 100, "GBP");
    }

    [Fact]
    public async Task GetPayment_UnknownPayment_Returns404NamingThePayment()
    {
        Guid paymentId = Guid.NewGuid();

        using HttpResponseMessage response = await this.fixture.Api.GetAsync($"api/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal($"Payment '{paymentId}' was not found.", problem.Detail);
    }

    private static void AssertMerchantResponse(JsonElement payment, string paymentId, string status, long amount, string currency)
    {
        Assert.Equal(
            new[] { "amount", "currencyCode", "paymentId", "paymentStatus" },
            payment.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(paymentId, payment.GetProperty("paymentId").GetString());
        Assert.Equal(status, payment.GetProperty("paymentStatus").GetString());
        Assert.Equal(amount, payment.GetProperty("amount").GetInt64());
        Assert.Equal(currency, payment.GetProperty("currencyCode").GetString());
    }

    private async Task<string> PostPayment(JsonObject request)
    {
        using HttpResponseMessage response = await this.fixture.Api.PostAsJsonAsync("api/payments", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payment.GetProperty("paymentId").GetString()!;
    }
}
