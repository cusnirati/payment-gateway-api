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
}
