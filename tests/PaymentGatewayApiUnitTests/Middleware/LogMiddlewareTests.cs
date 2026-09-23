using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PaymentGatewayApi.Middleware;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace PaymentGatewayApiUnitTests.Middleware;

public class LogMiddlewareTests
{
    private readonly CollectingSink sink = new();
    private readonly Logger logger;

    public LogMiddlewareTests()
    {
        this.logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(this.sink)
            .CreateLogger();
    }

    // runs the middleware with a next delegate that logs one event, and returns that event
    private async Task<LogEvent> InvokeAndCapture(HttpContext context)
    {
        LogMiddleware middleware = new(_ =>
        {
            this.logger.Information("next");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        return Assert.Single(this.sink.Events);
    }

    private static string? ScalarValue(LogEvent logEvent, string property)
    {
        Assert.True(logEvent.Properties.TryGetValue(property, out LogEventPropertyValue? value));
        return Assert.IsType<ScalarValue>(value).Value?.ToString();
    }

    [Fact]
    public async Task InvokeAsync_CallsNextOnce()
    {
        int calls = 0;
        LogMiddleware middleware = new(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvokeAsync_CorrelationIdHeader_IsPushedToLogContext()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["CorrelationId"] = "abc";

        LogEvent logEvent = await this.InvokeAndCapture(context);

        Assert.Equal("abc", ScalarValue(logEvent, "CorrelationId"));
    }

    [Fact]
    public async Task InvokeAsync_NoCorrelationIdHeader_GeneratesGuid()
    {
        LogEvent logEvent = await this.InvokeAndCapture(new DefaultHttpContext());

        Assert.True(Guid.TryParse(ScalarValue(logEvent, "CorrelationId"), out _));
    }

    [Fact]
    public async Task InvokeAsync_CallerIdHeader_IsPushedToLogContext()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["CallerId"] = "merchant-1";

        LogEvent logEvent = await this.InvokeAndCapture(context);

        Assert.True(logEvent.Properties.TryGetValue("CallerId", out LogEventPropertyValue? value));
        Assert.Contains("merchant-1", value.ToString());
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            this.Events.Add(logEvent);
        }
    }
}
