using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Services;

public class DynamoDbHealthCheck : IHealthCheck
{
    private readonly ILogger<DynamoDbHealthCheck> logger;
    private readonly IAmazonDynamoDB dynamo;

    public DynamoDbHealthCheck(ILogger<DynamoDbHealthCheck> logger, IAmazonDynamoDB dynamo)
    {
        this.logger = logger;
        this.dynamo = dynamo;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            string table = "PendingPayments";
            
            DescribeTableResponse response = await this.dynamo.DescribeTableAsync(table);
            if (response.Table.TableStatus != TableStatus.ACTIVE)
            {
                this.logger.LogWarning("Operation={Operation}, Status={Status}, Table={Table}, TableStatus={TableStatus}",
                    "CheckHealth", "Failed", table, response.Table.TableStatus.Value);
                return HealthCheckResult.Unhealthy($"Table '{table}' is {response.Table.TableStatus.Value}.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Operation={Operation}, Status={Status}", "CheckHealth", "Failed");
            return HealthCheckResult.Unhealthy("DynamoDB is unreachable or a payment table is missing.", exception);
        }
    }
}
