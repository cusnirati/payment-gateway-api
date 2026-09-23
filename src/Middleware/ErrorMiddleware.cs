using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Middleware;

public partial class ErrorMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<ErrorMiddleware> logger;

    public ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await this.next(context);
        }
        catch (Exception exception)
        {
            this.LogUnhandled(exception, "Failed");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            // see if can replace message with correlationId
            await context.Response.WriteAsync($"Unhandled exception\n{exception.Message}\n");

            return;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception, Status={Status}")]
    private partial void LogUnhandled(Exception exception, string status);
}
