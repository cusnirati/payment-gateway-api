using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Middleware;

// called by UseExceptionHandler, which has already cleared the response and set the 500 status;
// it isn't called once the response has started, the middleware rethrows so the server aborts the response
public class ErrorHandler : IExceptionHandler
{
    private readonly ILogger<ErrorHandler> logger;
    private readonly IProblemDetailsService problemDetailsService;

    public ErrorHandler(ILogger<ErrorHandler> logger, IProblemDetailsService problemDetailsService)
    {
        this.logger = logger;
        this.problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        this.logger.LogError(
            exception,
            "Operation={Operation}, Status={Status}",
            "UnhandledException", "Failed");

        return await this.problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, Exception = exception });
    }
}
