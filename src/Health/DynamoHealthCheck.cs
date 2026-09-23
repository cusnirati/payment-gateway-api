using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;

namespace PaymentGatewayApi.Health;

// ready means both tables can be reached and are active (an exception, e.g. DynamoDB being
// unreachable, is reported as unhealthy by the health check service); the bank has no
// side-effect-free endpoint to probe, its outages are handled per request by the circuit breaker
public class DynamoHealthCheck : IHealthCheck
{
    private readonly IAmazonDynamoDB dynamo;
    private readonly string[] tableNames;

    public DynamoHealthCheck(IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options)
    {
        this.dynamo = dynamo;
        this.tableNames = [options.Value.PaymentsTable, options.Value.IdempotencyKeysTable];
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        foreach (string tableName in this.tableNames)
        {
            DescribeTableResponse response = await this.dynamo.DescribeTableAsync(tableName, cancellationToken);
            if (response.Table.TableStatus != TableStatus.ACTIVE)
            {
                return HealthCheckResult.Unhealthy($"Table {tableName} is {response.Table.TableStatus}.");
            }
        }

        return HealthCheckResult.Healthy();
    }
}
