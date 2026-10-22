using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

// every error response is an RFC 9457 problem details body that points back to the request's trace
public class ProblemDetailsTests
{
    private readonly ApiFixture fixture;

    public ProblemDetailsTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData("GET", "/api/payment/6f1c0a39-5f4e-4b8e-9d0c-2b7f4a1e8c3d", null, null, 404)] // payment not found
    [InlineData("GET", "/api/payment/not-a-guid", null, null, 404)] // no route matches
    [InlineData("GET", "/api/unknown", null, null, 404)]
    [InlineData("DELETE", "/api/payment", null, null, 405)]
    [InlineData("POST", "/api/payment", "application/json", """{ "cardNumber": "2223000000000007", "expiryMonth": 4, "expiryYear": 2099, "amount": 100, "currency": "JPY", "cvv": "123" }""", 400)]
    [InlineData("POST", "/api/payment", "application/json", "{}", 400)] // fails model binding
    [InlineData("POST", "/api/payment", "text/plain", "payment", 415)]
    public async Task ErrorResponse_IsAProblemDetailsBodyWithInstanceAndTraceId(string method, string path, string? contentType, string? body, int status)
    {
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType!);
        }

        using HttpResponseMessage response = await this.fixture.Api.SendAsync(request);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.Equal(TraceResponse.TraceIdOf(response), problem.GetProperty("traceId").GetString());
    }
}
