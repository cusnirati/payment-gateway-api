using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace PaymentGatewayApi.Middleware;

// gives every request a correlation id: the caller's X-Correlation-ID when it is well formed, a new
// GUID otherwise. It is pushed onto every log line of the request and echoed on the response, so a
// merchant can quote it to support. The W3C trace id (traceparent) flows alongside it on its own.
public partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private static readonly object ItemKey = new();

    private readonly RequestDelegate next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public static string? Get(HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out object? value) ? value as string : null;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // an unauthenticated header lands in every log line: anything that is not a short token
        // (newlines, markup, kilobytes of padding) is replaced rather than trusted
        string header = context.Request.Headers[HeaderName].ToString();
        string correlationId = ValidCorrelationId().IsMatch(header) ? header : Guid.NewGuid().ToString();

        context.Items[ItemKey] = correlationId;

        // set when the response starts, so it survives the exception handler clearing the headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await this.next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ValidCorrelationId();
}
