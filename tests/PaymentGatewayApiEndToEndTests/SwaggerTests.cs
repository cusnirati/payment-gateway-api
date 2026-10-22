using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class SwaggerTests
{
    private readonly ApiFixture fixture;

    public SwaggerTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task SwaggerDocument_DescribesThePaymentEndpoints()
    {
        using HttpResponseMessage response = await this.fixture.Api.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement paths = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paths");
        Assert.True(paths.GetProperty("/api/Payment").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/Payment/{paymentId}").TryGetProperty("get", out _));
    }
}
