using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class DynamoDbHealthCheckTests
{
    private readonly Mock<IAmazonDynamoDB> dynamo = new();
    private readonly IHealthCheck healthCheck;

    public DynamoDbHealthCheckTests()
    {
        this.healthCheck = new DynamoDbHealthCheck(NullLogger<DynamoDbHealthCheck>.Instance, this.dynamo.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_PaymentsTableActive_ReturnsHealthy()
    {
        this.dynamo
            .Setup(d => d.DescribeTableAsync("Payments", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });

        HealthCheckResult result = await this.healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData("CREATING")]
    [InlineData("UPDATING")]
    [InlineData("DELETING")]
    public async Task CheckHealthAsync_PaymentsTableNotActive_ReturnsUnhealthy(string tableStatus)
    {
        this.dynamo
            .Setup(d => d.DescribeTableAsync("Payments", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = new TableStatus(tableStatus) } });

        HealthCheckResult result = await this.healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal($"Table 'Payments' is {tableStatus}.", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_DynamoDbFails_ReturnsUnhealthyWithTheException()
    {
        ResourceNotFoundException exception = new("Cannot do operations on a non-existent table");
        this.dynamo
            .Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        HealthCheckResult result = await this.healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("DynamoDB is unreachable or a payment table is missing.", result.Description);
        Assert.Same(exception, result.Exception);
    }
}
