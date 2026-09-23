using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PaymentGatewayApiIntegrationTests;

public static class HttpClientExtensions
{
    public static HttpClient CreateMerchantClient<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, string? apiKey = GatewayFactory.MerchantAKey)
        where TEntryPoint : class
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public static async Task<HttpResponseMessage> PostPayment(this HttpClient client, object payment, string? idempotencyKey = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "api/payments") { Content = JsonContent.Create(payment) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<JsonNode> ReadJson(this HttpResponseMessage response)
    {
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            ?? throw new InvalidOperationException("The response body is empty.");
    }
}
