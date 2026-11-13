using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class ExceptionHandlerTests
{
    private readonly Mock<ILogger<ExceptionHandler>> logger = new();
    private readonly Mock<IProblemDetailsService> problemDetailsService = new();
    private readonly DefaultHttpContext context = new();
    private readonly InvalidOperationException exception = new("Something failed");
    private readonly ExceptionHandler handler;

    public ExceptionHandlerTests()
    {
        this.handler = new ExceptionHandler(this.logger.Object, this.problemDetailsService.Object);
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
