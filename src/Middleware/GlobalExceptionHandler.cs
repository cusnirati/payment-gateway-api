using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Middleware;

// last resort for anything the pipeline did not handle: log it, answer a generic ProblemDetails
// with the trace id, and never echo the exception message (it can name hosts, tables, keys)
public partial class GlobalExceptionHandler : IExceptionHandler
{
    // nginx's convention for "client closed request", the client is gone and never reads it
    public const int ClientClosedRequest = 499;

    private readonly ILogger<GlobalExceptionHandler> logger;
    private readonly IProblemDetailsService problemDetailsService;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetailsService)
    {
        this.logger = logger;
        this.problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            this.LogClientAborted(httpContext.Request.Path, "Cancelled");
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        this.LogUnhandled(exception, httpContext.Request.Path, "Failed");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await this.problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Quote the traceId to support."
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception, Path={Path}, Status={Status}")]
    private partial void LogUnhandled(Exception exception, PathString path, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request aborted by the client, Path={Path}, Status={Status}")]
    private partial void LogClientAborted(PathString path, string status);
}
