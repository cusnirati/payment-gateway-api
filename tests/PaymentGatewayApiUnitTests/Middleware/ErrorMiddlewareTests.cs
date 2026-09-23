using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Middleware;
using Xunit;

namespace PaymentGatewayApiUnitTests.Middleware;

public class ErrorMiddlewareTests
{
    private static DefaultHttpContext NewContext()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task InvokeAsync_NextSucceeds_LeavesResponseUntouched()
    {
        DefaultHttpContext context = NewContext();
        ErrorMiddleware middleware = new(_ => Task.CompletedTask, NullLogger<ErrorMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(string.Empty, ReadBody(context));
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_Returns500WithMessage()
    {
        DefaultHttpContext context = NewContext();
        ErrorMiddleware middleware = new(
            _ => throw new InvalidOperationException("boom"), NullLogger<ErrorMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        string body = ReadBody(context);
        Assert.Contains("Unhandled exception", body);
        Assert.Contains("boom", body);
    }
}
