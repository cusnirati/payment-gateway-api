using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    [Fact]
    public async Task InvokeAsync_CorrelationIdHeader_IsEchoedInResponse()
    {
        TestResponseFeature response = new();
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Headers["CorrelationId"] = "abc";

        await this.InvokeAndCapture(context);
        await response.FireOnStarting();

        Assert.Equal("abc", context.Response.Headers["CorrelationId"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoCorrelationIdHeader_GeneratedGuidIsEchoedInResponse()
    {
        TestResponseFeature response = new();
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(response);

        LogEvent logEvent = await this.InvokeAndCapture(context);
        await response.FireOnStarting();

        string header = context.Response.Headers["CorrelationId"].ToString();
        Assert.True(Guid.TryParse(header, out _));
        Assert.Equal(ScalarValue(logEvent, "CorrelationId"), header);
    }

    [Fact]
    public async Task InvokeAsync_HeadersClearedBeforeResponseStarts_CorrelationIdStillSet()
    {
        TestResponseFeature response = new();
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Headers["CorrelationId"] = "abc";
        LogMiddleware middleware = new(ctx =>
        {
            // as an exception handler does before writing its own response
            ctx.Response.Headers.Clear();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await response.FireOnStarting();

        Assert.Equal("abc", context.Response.Headers["CorrelationId"].ToString());
    }

    // DefaultHttpContext's response feature ignores OnStarting, this one records the callbacks
    private class TestResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> onStarting = new();

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            this.onStarting.Add((callback, state));
        }

        public async Task FireOnStarting()
        {
            foreach ((Func<object, Task> callback, object state) in this.onStarting)
            {
                await callback(state);
            }
        }
    }

    private class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            this.Events.Add(logEvent);
        }
    }
}
