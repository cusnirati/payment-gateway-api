using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public class HealthTests
{
    private readonly ApiFixture fixture;

    public HealthTests(ApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthCheck_DependenciesRunning_ReturnsHealthy(string path)
    {
        using HttpResponseMessage response = await this.fixture.Api.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
