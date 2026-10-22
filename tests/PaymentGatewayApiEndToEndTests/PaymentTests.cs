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
        using HttpResponseMessage post = await this.fixture.Api.PostAsJsonAsync("api/payment", TestPayments.Create(cardNumber));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        JsonElement created = await post.Content.ReadFromJsonAsync<JsonElement>();
        string paymentId = created.GetProperty("paymentId").GetString()!;
        Assert.True(Guid.TryParse(paymentId, out _));
        Assert.Equal(status, created.GetProperty("paymentStatus").GetString());

        using HttpResponseMessage get = await this.fixture.Api.GetAsync($"api/payment/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        JsonElement retrieved = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(paymentId, retrieved.GetProperty("paymentId").GetString());
        Assert.Equal(status, retrieved.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task PostPayment_Authorized_StoresTheAuthorizationCodeButNeverTheFullCardNumberOrCvv()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        string paymentId = await this.PostPayment(TestPayments.Create(TestPayments.AuthorizedCard));
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Dictionary<string, AttributeValue>? item = await this.fixture.GetStoredItem(paymentId);
        Assert.NotNull(item);
        Assert.Equal(new[] { "CreatedAt", "PaymentId", "PaymentRaw" }, item.Keys.Order());
        Assert.InRange(DateTimeOffset.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture), before, after);

        string raw = item["PaymentRaw"].S;
        Assert.DoesNotContain(TestPayments.AuthorizedCard, raw);
        using JsonDocument stored = JsonDocument.Parse(raw);
        JsonElement payment = stored.RootElement;
        Assert.Equal(
            new[] { "Amount", "BankAuthorizationCode", "BankReason", "CardNumberLastFour", "Currency", "ExpiryMonth", "ExpiryYear", "PaymentId", "Status" },
            payment.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("0007", payment.GetProperty("CardNumberLastFour").GetString());
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
    }

    [Fact]
    public async Task PostPayment_PaymentIdFromTheMerchant_IsReplaced()
    {
        string merchantPaymentId = Guid.NewGuid().ToString();
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        request["paymentId"] = merchantPaymentId;

        string paymentId = await this.PostPayment(request);

        Assert.NotEqual(merchantPaymentId, paymentId);
        Assert.Empty(await this.fixture.FindBankRequests("$.paymentId", merchantPaymentId));
        Assert.Single(await this.fixture.FindBankRequests("$.paymentId", paymentId));
    }

    [Fact]
    public async Task PostPayment_ReadmePayload_IsAccepted()
    {
        // as in the README and the Bruno collection: property names in any case, numbers as strings
        string body = $$"""
            {
              "cardnumber": "{{TestPayments.AuthorizedCard}}",
              "ExpiryMonth": "11",
              "ExpiryYear": "{{DateTime.UtcNow.Year + 1}}",
              "currency": "GBP",
              "amount": 100,
              "cvv": "123"
            }
            """;

        using HttpResponseMessage response = await this.fixture.Api.PostAsync("api/payment", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Authorized", payment.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task GetPayment_UnknownPayment_Returns404NamingThePayment()
    {
        Guid paymentId = Guid.NewGuid();

        using HttpResponseMessage response = await this.fixture.Api.GetAsync($"api/payment/{paymentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal($"Payment '{paymentId}' was not found.", problem.Detail);
    }

    private async Task<string> PostPayment(JsonObject request)
    {
        using HttpResponseMessage response = await this.fixture.Api.PostAsJsonAsync("api/payment", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payment.GetProperty("paymentId").GetString()!;
    }
}
