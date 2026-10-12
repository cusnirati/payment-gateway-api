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
        // W3C trace context response header (00-traceId-spanId-flags), set before the pipeline runs,
        // headers are read-only once the response has started
        string? traceResponse = Activity.Current?.Id;
        if (traceResponse is not null)
        {
            context.Response.Headers["traceresponse"] = traceResponse;
        }

        // this is not a customer facing service in the DMZ so we skip some security checks like header validation and sanitization
        using (LogContext.PushProperty("CallerId", context.Request.Headers["CallerId"].FirstOrDefault()))
        {
            await this.next(context);
        }
    }
}
