using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace PaymentGatewayApiIntegrationTests;

// registered as an ILogEventSink service, which the app's ReadFrom.Services adds to its logger
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> events = new();

    public IReadOnlyList<LogEvent> Events => this.events.ToList();

    public void Emit(LogEvent logEvent)
    {
        this.events.Enqueue(logEvent);
    }
}
