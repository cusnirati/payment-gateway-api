using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class PaymentEndToEndTests
{
    private readonly HttpClient client;

    public PaymentEndToEndTests(ApiFixture fixture)
    {
        this.client = fixture.Client;
    }

    [Fact]
    public async Task Healthcheck_ReturnsOk()
    {
        HttpResponseMessage response = await this.client.GetAsync("api/payment", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostPayment_OddCard_AuthorizedAndRetrievable()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest(TestData.AuthorizedCard);

        HttpResponseMessage postResponse = await this.client.PostAsJsonAsync("api/payment", payment, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        string paymentId = await postResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(Guid.TryParse(paymentId, out _));

        HttpResponseMessage getResponse = await this.client.GetAsync($"api/payment/{paymentId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        string raw = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(TestData.AuthorizedCard, raw);

        PaymentDb? stored = await getResponse.Content.ReadFromJsonAsync<PaymentDb>(TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(paymentId, stored.PaymentId);
        Assert.Equal(PaymentStatus.Authorized, stored.Status);
        Assert.Equal("8873", stored.CardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, stored.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, stored.ExpiryYear);
        Assert.Equal(payment.Currency, stored.Currency);
        Assert.Equal(payment.Amount, stored.Amount);
    }

    [Fact]
    public async Task PostPayment_EvenCard_Declined()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest(TestData.DeclinedCard);

        HttpResponseMessage response = await this.client.PostAsJsonAsync("api/payment", payment, TestContext.Current.CancellationToken);

        await AssertBadRequest(response, PaymentStatus.Declined);
    }

    [Fact]
    public async Task PostPayment_CardEndingZero_InternalError()
    {
        // slow, the resilience handler retries the simulator's 503 before giving up
        PaymentRequest payment = TestData.ValidPaymentRequest(TestData.BankUnavailableCard);

        HttpResponseMessage response = await this.client.PostAsJsonAsync("api/payment", payment, TestContext.Current.CancellationToken);

        await AssertBadRequest(response, PaymentStatus.InternalError);
    }

    [Theory]
    [InlineData("CardNumber")]
    [InlineData("Expiry")]
    [InlineData("Amount")]
    [InlineData("Currency")]
    [InlineData("Cvv")]
    public async Task PostPayment_InvalidField_Rejected(string invalidField)
    {
        PaymentRequest payment = TestData.ValidPaymentRequest(TestData.AuthorizedCard);
        switch (invalidField)
        {
            case "CardNumber": payment.CardNumber = "1234"; break;
            case "Expiry": payment.ExpiryYear = DateTime.Now.Year - 1; break;
            case "Amount": payment.Amount = 0; break;
            case "Currency": payment.Currency = "GBPX"; break;
            case "Cvv": payment.Cvv = "12"; break;
        }

        HttpResponseMessage response = await this.client.PostAsJsonAsync("api/payment", payment, TestContext.Current.CancellationToken);

        await AssertBadRequest(response, PaymentStatus.Rejected);
    }

    [Fact]
    public async Task PostPayment_ExpiryYearZero_Rejected()
    {
        PaymentRequest payment = TestData.ValidPaymentRequest(TestData.AuthorizedCard);
        payment.ExpiryYear = 0;

        HttpResponseMessage response = await this.client.PostAsJsonAsync("api/payment", payment, TestContext.Current.CancellationToken);

        await AssertBadRequest(response, PaymentStatus.Rejected);
    }

    [Fact]
    public async Task GetPayment_UnknownId_ReturnsNotFound()
    {
        HttpResponseMessage response = await this.client.GetAsync($"api/payment/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AssertBadRequest(HttpResponseMessage response, PaymentStatus expected)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected.ToString(), await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
