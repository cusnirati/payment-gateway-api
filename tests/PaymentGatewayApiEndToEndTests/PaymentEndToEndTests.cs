using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class PaymentEndToEndTests
{
    private readonly ApiFixture fixture;
    private readonly HttpClient client;

    public PaymentEndToEndTests(ApiFixture fixture)
    {
        this.fixture = fixture;
        this.client = fixture.Client;
    }

    private Task<HttpResponseMessage> Post(PaymentBody payment)
    {
        return this.client.PostAsJsonAsync("api/payments", payment, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonNode> ReadJson(HttpResponseMessage response)
    {
        JsonNode? json = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(json);
        return json;
    }

    private static async Task<JsonNode> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonNode problem = await ReadJson(response);
        Assert.Equal((int)expected, problem["status"]?.GetValue<int>());
        Assert.NotNull(problem["traceId"]);
        return problem;
    }

    [Theory]
    [InlineData("health/live")]
    [InlineData("health/ready")] // the stack's DynamoDB table is reachable
    public async Task Health_ReturnsHealthy(string path)
    {
        HttpResponseMessage response = await this.fixture.Anonymous.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenApi_NotServedOutsideDevelopment()
    {
        // the container runs in Production; asked anonymously, any unmapped path is a 401 like every other
        // endpoint that does not opt out of authentication
        HttpResponseMessage response = await this.client.GetAsync("openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(TestData.AuthorizedCard, "Authorized")]
    [InlineData(TestData.DeclinedCard, "Declined")]
    public async Task PostPayment_BankAnswers_CreatedAndRetrievable(string cardNumber, string expectedStatus)
    {
        PaymentBody payment = TestData.ValidPayment(cardNumber);

        HttpResponseMessage postResponse = await this.Post(payment);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        JsonNode created = await ReadJson(postResponse);
        Guid paymentId = Guid.Parse(created["id"]!.GetValue<string>());
        Assert.Equal(expectedStatus, created["status"]?.GetValue<string>());
        Assert.NotNull(postResponse.Headers.Location);
        Assert.EndsWith($"/api/payments/{paymentId}", postResponse.Headers.Location.ToString(), StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage getResponse = await this.client.GetAsync(postResponse.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        string raw = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(cardNumber, raw);
        Assert.DoesNotContain("cvv", raw, StringComparison.OrdinalIgnoreCase);

        JsonNode stored = JsonNode.Parse(raw)!;
        Assert.Equal(paymentId.ToString(), stored["id"]?.GetValue<string>());
        Assert.Equal(expectedStatus, stored["status"]?.GetValue<string>());
        Assert.Equal(cardNumber[^4..], stored["cardNumberLastFour"]?.GetValue<string>());
        Assert.Equal(payment.ExpiryMonth, stored["expiryMonth"]?.GetValue<int>());
        Assert.Equal(payment.ExpiryYear, stored["expiryYear"]?.GetValue<int>());
        Assert.Equal(payment.Currency, stored["currency"]?.GetValue<string>());
        Assert.Equal(payment.Amount, stored["amount"]?.GetValue<int>());
    }

    [Fact]
    public async Task PostPayment_SingleDigitExpiryMonth_Authorized()
    {
        // the simulator only answers a "MM/yyyy" expiry, so a month sent as "4/..." would fall through to its 400
        HttpResponseMessage response = await this.Post(TestData.ValidPayment(TestData.AuthorizedCard) with { ExpiryMonth = 4 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Authorized", (await ReadJson(response))["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task PostPayment_BankUnavailable_BadGatewayWithFailedPaymentOnRecord()
    {
        HttpResponseMessage response = await this.Post(TestData.ValidPayment(TestData.BankUnavailableCard));

        JsonNode problem = await AssertProblem(response, HttpStatusCode.BadGateway);
        Assert.Equal("Failed", problem["paymentStatus"]?.GetValue<string>());

        string paymentId = problem["paymentId"]!.GetValue<string>();
        HttpResponseMessage getResponse = await this.client.GetAsync($"api/payments/{paymentId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("Failed", (await ReadJson(getResponse))["status"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("cardNumber")]
    [InlineData("expiryMonth")]
    [InlineData("expiryYear")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("cvv")]
    public async Task PostPayment_InvalidField_ValidationProblemNamingTheField(string field)
    {
        PaymentBody payment = TestData.ValidPayment(TestData.AuthorizedCard);
        payment = field switch
        {
            "cardNumber" => payment with { CardNumber = "1234" },
            "expiryMonth" => payment with { ExpiryMonth = 13 },
            "expiryYear" => payment with { ExpiryYear = 0 },
            "amount" => payment with { Amount = 0 },
            "currency" => payment with { Currency = "JPY" },
            _ => payment with { Cvv = "12" }
        };

        HttpResponseMessage response = await this.Post(payment);

        JsonNode problem = await AssertProblem(response, HttpStatusCode.BadRequest);
        JsonObject errors = problem["errors"]!.AsObject();
        Assert.Single(errors);
        Assert.True(errors.ContainsKey(field));
    }

    [Theory]
    [InlineData("""{"expiryMonth":11,"expiryYear":2030,"currency":"GBP","amount":1050,"cvv":"123"}""")] // no card number
    [InlineData("""{"cardNumber":null,"expiryMonth":11,"expiryYear":2030,"currency":"GBP","amount":1050,"cvv":"123"}""")]
    [InlineData("""{"cardNumber":"2222405343248873","expiryMonth":11,"expiryYear":2030,"currency":"GBP","amount":10.5,"cvv":"123"}""")]
    public async Task PostPayment_MalformedBody_ValidationProblem(string body)
    {
        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await this.client.PostAsync("api/payments", content, TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPayment_UnknownId_NotFoundProblem()
    {
        HttpResponseMessage response = await this.client.GetAsync($"api/payments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPayment_MalformedId_NotFound()
    {
        HttpResponseMessage response = await this.client.GetAsync("api/payments/not-a-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-configured-key")]
    public async Task PostPayment_WithoutAValidApiKey_Unauthorized(string? apiKey)
    {
        using HttpClient caller = this.fixture.CreateClient(apiKey);

        HttpResponseMessage response = await caller.PostAsJsonAsync(
            "api/payments", TestData.ValidPayment(TestData.AuthorizedCard), TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
    }

    [Fact]
    public async Task GetPayment_AnotherMerchantsPayment_NotFound()
    {
        HttpResponseMessage created = await this.Post(TestData.ValidPayment(TestData.AuthorizedCard));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using HttpClient merchantB = this.fixture.CreateClient(ApiFixture.MerchantBKey);

        HttpResponseMessage response = await merchantB.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken);

        // indistinguishable from an id that does not exist, so ids cannot be probed
        await AssertProblem(response, HttpStatusCode.NotFound);
    }
}
