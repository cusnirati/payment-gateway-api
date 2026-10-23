using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly DefaultHttpContext context = new();
    private readonly StartableResponseFeature responseFeature = new();

    public LogMiddlewareTests()
    {
        this.context.Features.Set<IHttpResponseFeature>(this.responseFeature);
    }

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
        await this.responseFeature.StartAsync();

        Assert.True(nextCalled);
        Assert.Equal(activity.Id, this.context.Response.Headers["traceresponse"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_ResponseClearedDownstream_StillReturnsTheTraceResponseHeader()
    {
        using Activity activity = new Activity("request").Start();
        // UseExceptionHandler clears the response, headers included, before writing the problem
        LogMiddleware middleware = new(context =>
        {
            context.Response.Clear();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(this.context);
        await this.responseFeature.StartAsync();

        Assert.Equal(activity.Id, this.context.Response.Headers["traceresponse"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoCurrentActivity_LeavesOutTheTraceResponseHeader()
    {
        Activity.Current = null;
        LogMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(this.context);
        await this.responseFeature.StartAsync();

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

// DefaultHttpContext's response feature drops OnStarting callbacks, this one keeps them so the test can start the response
public class StartableResponseFeature : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> onStarting = new();

    public override void OnStarting(Func<object, Task> callback, object state)
    {
        this.onStarting.Add((callback, state));
    }

    public async Task StartAsync()
    {
        foreach ((Func<object, Task> callback, object state) in this.onStarting)
        {
            await callback(state);
        }
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
