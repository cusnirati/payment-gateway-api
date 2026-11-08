using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class ValidationTests
{
    private readonly ApiFixture fixture;

    public ValidationTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData("cardNumber", "\"4111111111111112\"", "CardNumber")] // fails the Luhn checksum
    [InlineData("cardNumber", "\"4111\"", "CardNumber")]
    [InlineData("cardNumber", "\"4111-1111-1111-1111\"", "CardNumber")]
    [InlineData("expiryMonth", "13", "ExpiryMonth")]
    [InlineData("expiryMonth", "0", "ExpiryMonth")]
    [InlineData("expiryYear", "27", "ExpiryYear")]
    [InlineData("cvv", "\"12\"", "Cvv")]
    [InlineData("cvv", "\"12345\"", "Cvv")]
    [InlineData("amount", "0", "Amount")]
    [InlineData("amount", "-100", "Amount")]
    [InlineData("currency", "\"JPY\"", "Currency")]
    public async Task PostPayment_OneInvalidField_Returns400WithOnlyThatError(string property, string json, string error)
    {
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        request[property] = JsonNode.Parse(json);

        ValidationProblemDetails problem = await this.PostInvalidPayment(request);

        Assert.Equal(new[] { error }, problem.Errors.Keys);
        Assert.NotEmpty(problem.Errors[error]);
    }

    [Fact]
    public async Task PostPayment_ExpiredCard_Returns400WithExpiryError()
    {
        DateTime lastMonth = DateTime.UtcNow.AddMonths(-1);
        JsonObject request = TestPayments.Create(TestPayments.AuthorizedCard);
        request["expiryMonth"] = lastMonth.Month;
        request["expiryYear"] = lastMonth.Year;

        ValidationProblemDetails problem = await this.PostInvalidPayment(request);

        Assert.Equal(new[] { "ExpiryYear" }, problem.Errors.Keys);
    }

    [Fact]
    public async Task PostPayment_EveryFieldInvalid_Returns400WithEveryError()
    {
        JsonObject request = new()
        {
            ["paymentId"] = Guid.NewGuid().ToString(),
            ["cardNumber"] = "1234",
            ["expiryMonth"] = 13,
            ["expiryYear"] = DateTime.UtcNow.Year + 1,
            ["amount"] = 0,
            ["currency"] = "XXX",
            ["cvv"] = "1"
        };

        ValidationProblemDetails problem = await this.PostInvalidPayment(request);

        Assert.Equal(new[] { "Amount", "CardNumber", "Currency", "Cvv", "ExpiryMonth" }, problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task PostPayment_InvalidPayment_NeverReachesTheBank()
    {
        // amounts no other test uses, so the bank simulator's request journal can be searched for them
        long invalidAmount = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        long validAmount = invalidAmount + 1;
        JsonObject invalid = TestPayments.Create(TestPayments.AuthorizedCard);
        invalid["amount"] = invalidAmount;
        invalid["currency"] = "JPY";
        JsonObject valid = TestPayments.Create(TestPayments.AuthorizedCard);
        valid["amount"] = validAmount;

        using HttpResponseMessage rejected = await this.fixture.Api.PostAsJsonAsync("api/payments", invalid);
        using HttpResponseMessage accepted = await this.fixture.Api.PostAsJsonAsync("api/payments", valid);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Empty(await this.fixture.FindBankRequests("$.amount", invalidAmount.ToString(CultureInfo.InvariantCulture)));
        // the valid one proves the search finds a payment by its amount
        Assert.Single(await this.fixture.FindBankRequests("$.amount", validAmount.ToString(CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("""{ "cardNumber": """)]
    [InlineData("""{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": 100, "currency": "GBP", "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d" }""")]
    [InlineData("""{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": 100, "currency": null, "cvv": "123", "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d" }""")]
    [InlineData("""{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": "abc", "currency": "GBP", "cvv": "123", "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d" }""")]
    // no paymentId, the merchant must supply the idempotency key
    [InlineData("""{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": 100, "currency": "GBP", "cvv": "123" }""")]
    [InlineData("""{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": 100, "currency": "GBP", "cvv": "123", "paymentId": "not-a-guid" }""")]
    public async Task PostPayment_UnreadableBody_Returns400Problem(string body)
    {
        using HttpResponseMessage response = await this.fixture.Api.PostAsync("api/payments", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task PostPayment_NotJson_Returns415Problem()
    {
        using HttpResponseMessage response = await this.fixture.Api.PostAsync(
            "api/payments", new StringContent($"cardNumber={TestPayments.AuthorizedCard}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<ValidationProblemDetails> PostInvalidPayment(JsonObject request)
    {
        using HttpResponseMessage response = await this.fixture.Api.PostAsJsonAsync("api/payments", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        return problem;
    }
}
