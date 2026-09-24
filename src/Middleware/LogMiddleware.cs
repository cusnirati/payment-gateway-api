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
            // or return bad request if missing correlationId
            correlationId = Guid.NewGuid().ToString();
        }

        // set when the response starts, so it survives an exception handler clearing the headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["CorrelationId"] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CallerId", context.Request.Headers["CallerId"]))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
