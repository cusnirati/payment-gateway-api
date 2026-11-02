using System;
using Microsoft.Extensions.Logging;
using Moq;

namespace PaymentGatewayApiUnitTests;

public static class LoggerMockExtensions
{
    // LogError, LogWarning, ... are extension methods that all end in ILogger.Log, the only call a mock can see
    public static void VerifyLogged<T>(this Mock<ILogger<T>> logger, LogLevel level, Exception exception)
    {
        logger.Verify(
            l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), exception, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // for logs written without an exception
    public static void VerifyLogged<T>(this Mock<ILogger<T>> logger, LogLevel level)
    {
        logger.Verify(
            l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    public static void VerifyNotLogged<T>(this Mock<ILogger<T>> logger, LogLevel level)
    {
        logger.Verify(
            l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
