using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Responses;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentsRepositoryTests
{
    private readonly Mock<ILogger<PaymentsRepository>> logger = new();
    private readonly Mock<IAmazonDynamoDB> dynamo = new();
    private readonly Mock<TimeProvider> timeProvider = new();
    private readonly PaymentsRepository repository;

    private readonly PaymentDb payment = new()
    {
        PaymentId = "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d",
        CardNumberLastFour = "0007",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Amount = 100,
        Currency = "GBP",
        Status = PaymentStatus.Authorized,
        BankAuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
    };

    public PaymentsRepositoryTests()
    {
        this.timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 10, 22, 9, 30, 15, TimeSpan.Zero));
        this.repository = new PaymentsRepository(this.logger.Object, this.dynamo.Object, this.timeProvider.Object);
    }

    [Fact]
    public async Task Put_WritesThePaymentWithItsIdAndCreationTimeToThePaymentsTable()
    {
        Dictionary<string, AttributeValue>? item = null;
        this.dynamo
            .Setup(d => d.PutItemAsync("Payments", It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, AttributeValue>, CancellationToken>((_, written, _) => item = written)
            .ReturnsAsync(new PutItemResponse());

        await this.repository.Put(this.payment);

        Assert.NotNull(item);
        Assert.Equal(new[] { "CreatedAt", "PaymentId", "PaymentRaw" }, item.Keys.Order());
        Assert.Equal(this.payment.PaymentId, item["PaymentId"].S);
        Assert.Equal("2026-10-22T09:30:15.0000000Z", item["CreatedAt"].S);
        using JsonDocument raw = JsonDocument.Parse(item["PaymentRaw"].S);
        Assert.Equal("Authorized", raw.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Put_DynamoDbFails_LogsTheErrorWithoutThrowing()
    {
        AmazonDynamoDBException exception = new("Throughput exceeds the current capacity");
        this.dynamo
            .Setup(d => d.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        Exception? thrown = await Record.ExceptionAsync(() => this.repository.Put(this.payment));

        Assert.Null(thrown);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }

    [Fact]
    public async Task Get_StoredPayment_ReturnsIt()
    {
        // PaymentRaw written out literally: payments already in the table must stay readable
        string raw = """
            {"CardNumberLastFour":"0007","ExpiryMonth":4,"ExpiryYear":2030,"Amount":100,"Currency":"GBP",
            "PaymentId":"1bfb4527-35b5-4a0c-84d1-c3227fd63b5d","Status":"Authorized",
            "BankAuthorizationCode":"0bb07405-6d44-4b50-a14f-7ae0beff13ad","BankReason":null}
            """;
        this.dynamo
            .Setup(d => d.GetItemAsync(
                "Payments",
                It.Is<Dictionary<string, AttributeValue>>(key => key.Count == 1 && key["PaymentId"].S == this.payment.PaymentId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PaymentId"] = new AttributeValue { S = this.payment.PaymentId },
                    ["PaymentRaw"] = new AttributeValue { S = raw },
                    ["CreatedAt"] = new AttributeValue { S = "2026-10-22T09:30:15.0000000Z" }
                }
            });

        PaymentDb? stored = await this.repository.Get(this.payment.PaymentId!);

        Assert.Equivalent(this.payment, stored, strict: true);
    }

    [Fact]
    public async Task Get_ReadsBackWhatPutWrote()
    {
        Dictionary<string, AttributeValue>? item = null;
        this.dynamo
            .Setup(d => d.PutItemAsync("Payments", It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, AttributeValue>, CancellationToken>((_, written, _) => item = written)
            .ReturnsAsync(new PutItemResponse());
        this.dynamo
            .Setup(d => d.GetItemAsync("Payments", It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new GetItemResponse { Item = item });

        await this.repository.Put(this.payment);
        PaymentDb? stored = await this.repository.Get(this.payment.PaymentId!);

        Assert.Equivalent(this.payment, stored, strict: true);
    }

    [Fact]
    public async Task Get_UnknownPayment_ReturnsNull()
    {
        this.dynamo
            .Setup(d => d.GetItemAsync("Payments", It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });

        PaymentDb? stored = await this.repository.Get("6f1c0a39-5f4e-4b8e-9d0c-2b7f4a1e8c3d");

        Assert.Null(stored);
    }

    [Fact]
    public async Task Get_EmptyItem_ReturnsNull()
    {
        this.dynamo
            .Setup(d => d.GetItemAsync("Payments", It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        PaymentDb? stored = await this.repository.Get("6f1c0a39-5f4e-4b8e-9d0c-2b7f4a1e8c3d");

        Assert.Null(stored);
    }

    [Fact]
    public async Task Get_DynamoDbFails_LogsAndRethrows()
    {
        AmazonDynamoDBException exception = new("Unable to connect to the remote server");
        this.dynamo
            .Setup(d => d.GetItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        AmazonDynamoDBException thrown = await Assert.ThrowsAsync<AmazonDynamoDBException>(() => this.repository.Get(this.payment.PaymentId!));

        Assert.Same(exception, thrown);
        this.logger.VerifyLogged(LogLevel.Error, exception);
    }
}
