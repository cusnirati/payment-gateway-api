using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;

namespace PaymentGatewayApi.Health;

// ready means the payments table can be reached and is active (an exception, e.g. DynamoDB being
// unreachable, is reported as unhealthy by the health check service); the bank has no
// side-effect-free endpoint to probe, its outages are handled per request by the circuit breaker
public class DynamoHealthCheck : IHealthCheck
{
    private readonly IAmazonDynamoDB dynamo;
    private readonly string tableName;

    public DynamoHealthCheck(IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options)
    {
        this.dynamo = dynamo;
        this.tableName = options.Value.PaymentsTable;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DescribeTableResponse response = await this.dynamo.DescribeTableAsync(this.tableName, cancellationToken);

        return response.Table.TableStatus == TableStatus.ACTIVE
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Table status is {response.Table.TableStatus}.");
    }
}
