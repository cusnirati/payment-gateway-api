using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGatewayApi.Middleware;
using Xunit;

namespace PaymentGatewayApiUnitTests.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();

    private GlobalExceptionHandler NewHandler()
    {
        return new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance, this.services.GetRequiredService<IProblemDetailsService>());
    }

    private DefaultHttpContext NewContext()
    {
        return new DefaultHttpContext { RequestServices = this.services, Response = { Body = new MemoryStream() } };
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task TryHandleAsync_Exception_Returns500ProblemWithoutTheMessage()
    {
        DefaultHttpContext context = this.NewContext();

        bool handled = await this.NewHandler().TryHandleAsync(
            context, new InvalidOperationException("table Payments on dynamodb.internal:8000"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        string body = ReadBody(context);
        Assert.DoesNotContain("dynamodb", body, StringComparison.OrdinalIgnoreCase);
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task TryHandleAsync_ClientAborted_AnswersWithoutABody()
    {
        using CancellationTokenSource aborted = new();
        await aborted.CancelAsync();
        DefaultHttpContext context = this.NewContext();
        context.RequestAborted = aborted.Token;

        bool handled = await this.NewHandler().TryHandleAsync(
            context, new OperationCanceledException(aborted.Token), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(GlobalExceptionHandler.ClientClosedRequest, context.Response.StatusCode);
        Assert.Equal(string.Empty, ReadBody(context));
    }
}
