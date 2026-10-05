using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace PaymentGatewayApi.Middleware;

public class LogMiddleware
{
    private readonly RequestDelegate next;

    public LogMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string? correlationId;
        string? correlationHeader = context.Request.Headers["CorrelationId"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationHeader))
        {
            correlationId = correlationHeader;
        }
        else 
        {
            correlationId = Guid.NewGuid().ToString();
        }

        // set before the pipeline runs, headers are read-only once the response has started
        context.Response.Headers["CorrelationId"] = correlationId;

        // this is not a customer facing service in the DMZ so we skip some security checks like header validation and sanitization
        using (LogContext.PushProperty("CallerId", context.Request.Headers["CallerId"].FirstOrDefault()))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
