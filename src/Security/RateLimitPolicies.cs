using System;
using System.Globalization;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;

namespace PaymentGatewayApi.Security;

public static class RateLimitPolicies
{
    public const string PaymentsPerMerchant = "payments-per-merchant";

    // a fixed window per authenticated merchant, so one merchant's burst cannot starve the others
    public static IServiceCollection AddPaymentRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(PaymentsPerMerchant, context =>
            {
                RateLimitOptions limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                string partition = context.User.FindMerchantId() ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = limits.Window,
                    QueueLimit = 0
                });
            });

            options.OnRejected = RejectWithProblem;
        });
    }

    private static async ValueTask RejectWithProblem(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many payment requests.",
                Detail = "Retry after the time given in the Retry-After header."
            }
        });
    }
}
