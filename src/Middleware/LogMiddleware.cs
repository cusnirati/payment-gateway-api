using System;
using System.Diagnostics;
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
        // W3C trace context response header (00-traceId-spanId-flags), added right before the headers are sent
        // so it survives UseExceptionHandler clearing the response
        string? traceResponse = Activity.Current?.Id;
        if (traceResponse is not null)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["traceresponse"] = traceResponse;
                return Task.CompletedTask;
            });
        }

        // this is not a customer facing service in the DMZ so we skip some security checks like header validation and sanitization
        using (LogContext.PushProperty("CallerId", context.Request.Headers["CallerId"].FirstOrDefault()))
        {
            await this.next(context);
        }
    }
}
