using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Middleware;
using Xunit;

namespace PaymentGatewayApiUnitTests.Middleware;

public class ErrorMiddlewareTests
{
    private readonly Mock<ILogger<ErrorMiddleware>> logger = new();
    private readonly Mock<IProblemDetailsService> problemDetailsService = new();
    private readonly DefaultHttpContext context = new();
    private readonly InvalidOperationException exception = new("Something failed");

    public ErrorMiddlewareTests()
    {
        this.problemDetailsService.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task InvokeAsync_NoException_LeavesTheResponseAlone()
    {
        bool nextCalled = false;
        ErrorMiddleware middleware = new(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            this.logger.Object,
            this.problemDetailsService.Object);

        await middleware.InvokeAsync(this.context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, this.context.Response.StatusCode);
        this.problemDetailsService.Verify(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Writes500ProblemAndLogsIt()
    {
        ErrorMiddleware middleware = new(_ => throw this.exception, this.logger.Object, this.problemDetailsService.Object);

        await middleware.InvokeAsync(this.context);

        Assert.Equal(StatusCodes.Status500InternalServerError, this.context.Response.StatusCode);
        this.problemDetailsService.Verify(
            s => s.TryWriteAsync(It.Is<ProblemDetailsContext>(c => c.HttpContext == this.context && c.Exception == this.exception)),
            Times.Once);
        this.logger.VerifyLogged(LogLevel.Error, this.exception);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionAfterTheResponseStarted_RethrowsWithoutWritingAProblem()
    {
        Mock<IHttpResponseFeature> startedResponse = new();
        startedResponse.SetupGet(f => f.HasStarted).Returns(true);
        this.context.Features.Set(startedResponse.Object);
        ErrorMiddleware middleware = new(_ => throw this.exception, this.logger.Object, this.problemDetailsService.Object);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(this.context));

        Assert.Same(this.exception, thrown);
        this.problemDetailsService.Verify(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()), Times.Never);
        this.logger.VerifyLogged(LogLevel.Error, this.exception);
    }
}
