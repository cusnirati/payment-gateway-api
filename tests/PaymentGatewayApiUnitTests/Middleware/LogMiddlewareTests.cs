using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly DefaultHttpContext context = new();

    [Fact]
    public async Task InvokeAsync_CurrentActivity_ReturnsItsTraceContextInTheTraceResponseHeader()
    {
        using Activity activity = new Activity("request").Start();
        bool nextCalled = false;
        LogMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(this.context);

        Assert.True(nextCalled);
        Assert.Equal(activity.Id, this.context.Response.Headers["traceresponse"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoCurrentActivity_LeavesOutTheTraceResponseHeader()
    {
        Activity.Current = null;
        LogMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(this.context);

        Assert.False(this.context.Response.Headers.ContainsKey("traceresponse"));
    }

    [Fact]
    public async Task InvokeAsync_CallerIdHeader_IsOnEveryLogOfTheRequestOnly()
    {
        LogEventCollector collector = new();
        using Logger serilog = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(collector).CreateLogger();
        this.context.Request.Headers["CallerId"] = "merchant-42";
        LogMiddleware middleware = new(_ =>
        {
            serilog.Information("Inside the request");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(this.context);
        serilog.Information("After the request");

        Assert.Equal(2, collector.Events.Count);
        ScalarValue callerId = Assert.IsType<ScalarValue>(collector.Events[0].Properties["CallerId"]);
        Assert.Equal("merchant-42", callerId.Value);
        Assert.False(collector.Events[1].Properties.ContainsKey("CallerId"));
    }
}

public class LogEventCollector : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        this.Events.Add(logEvent);
    }
}
