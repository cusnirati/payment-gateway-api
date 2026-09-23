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

public class CorrelationIdMiddlewareTests
{
    private readonly CollectingSink sink = new();
    private readonly Logger logger;

    public CorrelationIdMiddlewareTests()
    {
        this.logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(this.sink)
            .CreateLogger();
    }

    private static DefaultHttpContext NewContext(string? header = null)
    {
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(new StartableResponseFeature());
        if (header is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = header;
        }

        return context;
    }

    // runs the middleware with a next delegate that logs one event and starts the response
    private async Task<string?> InvokeAndCapture(DefaultHttpContext context)
    {
        CorrelationIdMiddleware middleware = new(_ =>
        {
            this.logger.Information("next");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await ((StartableResponseFeature)context.Features.Get<IHttpResponseFeature>()!).StartAsync();

        LogEvent logEvent = Assert.Single(this.sink.Events);
        Assert.True(logEvent.Properties.TryGetValue("CorrelationId", out LogEventPropertyValue? value));
        return Assert.IsType<ScalarValue>(value).Value?.ToString();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("16bd4749-d150-48d1-a3ce-257457d74d1f")]
    public async Task InvokeAsync_WellFormedHeader_IsLoggedStoredAndEchoed(string header)
    {
        DefaultHttpContext context = NewContext(header);

        string? logged = await this.InvokeAndCapture(context);

        Assert.Equal(header, logged);
        Assert.Equal(header, CorrelationIdMiddleware.Get(context));
        Assert.Equal(header, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc\ninjected log line")]
    [InlineData("<script>")]
    public async Task InvokeAsync_MissingOrMalformedHeader_IsReplacedWithGuid(string? header)
    {
        DefaultHttpContext context = NewContext(header);

        string? logged = await this.InvokeAndCapture(context);

        Assert.True(Guid.TryParse(logged, out _));
        Assert.Equal(logged, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_OverlongHeader_IsReplacedWithGuid()
    {
        string? logged = await this.InvokeAndCapture(NewContext(new string('a', 65)));

        Assert.True(Guid.TryParse(logged, out _));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            this.Events.Add(logEvent);
        }
    }

    // DefaultHttpContext never starts its response, so OnStarting callbacks are run by hand
    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> callbacks = new();

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            this.callbacks.Add((callback, state));
        }

        public async Task StartAsync()
        {
            foreach ((Func<object, Task> callback, object state) in this.callbacks)
            {
                await callback(state);
            }
        }
    }
}
