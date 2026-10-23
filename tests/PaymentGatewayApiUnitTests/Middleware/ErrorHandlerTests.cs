using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Middleware;
using Xunit;

namespace PaymentGatewayApiUnitTests.Middleware;

public class ErrorHandlerTests
{
    private readonly Mock<ILogger<ErrorHandler>> logger = new();
    private readonly Mock<IProblemDetailsService> problemDetailsService = new();
    private readonly DefaultHttpContext context = new();
    private readonly InvalidOperationException exception = new("Something failed");
    private readonly ErrorHandler handler;

    public ErrorHandlerTests()
    {
        this.handler = new ErrorHandler(this.logger.Object, this.problemDetailsService.Object);
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_WritesAProblemAndLogsIt()
    {
        this.problemDetailsService.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>())).ReturnsAsync(true);

        bool handled = await this.handler.TryHandleAsync(this.context, this.exception, CancellationToken.None);

        Assert.True(handled);
        this.problemDetailsService.Verify(
            s => s.TryWriteAsync(It.Is<ProblemDetailsContext>(c => c.HttpContext == this.context && c.Exception == this.exception)),
            Times.Once);
        this.logger.VerifyLogged(LogLevel.Error, this.exception);
    }

    [Fact]
    public async Task TryHandleAsync_ProblemNotWritten_ReturnsFalse()
    {
        this.problemDetailsService.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>())).ReturnsAsync(false);

        bool handled = await this.handler.TryHandleAsync(this.context, this.exception, CancellationToken.None);

        Assert.False(handled);
        this.logger.VerifyLogged(LogLevel.Error, this.exception);
    }
}
