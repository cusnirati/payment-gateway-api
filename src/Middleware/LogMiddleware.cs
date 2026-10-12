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
        // this is not a customer facing service in the DMZ so we skip some security checks like header validation and sanitization
        using (LogContext.PushProperty("X-Client-Id", context.Request.Headers["X-Client-Id"].FirstOrDefault()))
        {
            await this.next(context);
        }
    }
}
