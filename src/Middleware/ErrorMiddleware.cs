using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Middleware;

public class ErrorMiddleware
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
            await next(context);
        }
        catch (Exception exception)
        {
            this.logger.LogError(
                exception,
                "Operation={Operation}, Status={Status}",
                "UnhandledException", "Failed");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            // or replace message with correlationId
            await context.Response.WriteAsync("InternalServerError");
        }
    }
}