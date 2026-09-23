using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services;

public class PaymentServiceTests
{
    private readonly Mock<IBankClient> bankClient = new();
    private readonly Mock<IPaymentsRepository> paymentRepo = new();
    private readonly FakeTimeProvider timeProvider = new(TestData.Now);
    private readonly PaymentService service;

    public PaymentServiceTests()
    {
        this.service = new PaymentService(
            NullLogger<PaymentService>.Instance, this.bankClient.Object, this.paymentRepo.Object, this.timeProvider);
    }

    private void SetupBank(BankOutcome outcome)
    {
        this.bankClient
            .Setup(c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
    }

    [Fact]
    public async Task ProcessPayment_StoresPendingPaymentBeforeCallingBank()
    {
        List<string> calls = new();
        Payment? added = null;
        this.paymentRepo
            .Setup(r => r.Add(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((payment, _) =>
            {
                calls.Add("add");
                added = payment;
            });
        this.bankClient
            .Setup(c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("bank"))
            .ReturnsAsync(BankOutcome.Declined);

        await this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(["add", "bank"], calls);
        Assert.NotNull(added);
        Assert.Equal(PaymentStatus.Pending, added.Status);
        Assert.Equal(TestData.MerchantId, added.MerchantId);
        Assert.Equal("8873", added.CardNumberLastFour);
        Assert.Equal(TestData.Now, added.CreatedAt);
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Unknown)]
    public async Task ProcessPayment_RecordsAndReturnsTheBankOutcome(PaymentStatus status)
    {
        BankOutcome outcome = status switch
        {
            PaymentStatus.Authorized => BankOutcome.Authorized("auth-code"),
            PaymentStatus.Declined => BankOutcome.Declined,
            PaymentStatus.Failed => BankOutcome.Failed,
            _ => BankOutcome.Unknown
        };
        this.SetupBank(outcome);
        this.bankClient
            .Setup(c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => this.timeProvider.Advance(TimeSpan.FromSeconds(2)))
            .ReturnsAsync(outcome);

        Payment result = await this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(outcome.AuthorizationCode, result.AuthorizationCode);
        Assert.Equal(TestData.Now, result.CreatedAt);
        Assert.Equal(TestData.Now.AddSeconds(2), result.UpdatedAt);
        this.paymentRepo.Verify(r => r.Complete(result, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessPayment_PendingWriteFails_ThrowsWithoutCallingBank()
    {
        this.paymentRepo
            .Setup(r => r.Add(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("unavailable"));

        await Assert.ThrowsAsync<AmazonDynamoDBException>(
            () => this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken));

        this.bankClient.Verify(
            c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_OutcomeWriteFails_StillReturnsTheBankOutcome()
    {
        this.SetupBank(BankOutcome.Authorized("auth-code"));
        this.paymentRepo
            .Setup(r => r.Complete(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("unavailable"));

        Payment result = await this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task ProcessPayment_CancelledBeforeBankCall_ThrowsWithoutCallingBank()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), cancelled.Token));

        this.bankClient.Verify(
            c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_BankCallIgnoresTheRequestToken()
    {
        this.SetupBank(BankOutcome.Declined);

        await this.service.ProcessPayment(TestData.MerchantId, TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        this.bankClient.Verify(
            c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FindPayment_ReturnsStoredPayment()
    {
        Payment stored = TestData.Payment(PaymentStatus.Authorized);
        this.paymentRepo.Setup(r => r.Find(TestData.PaymentId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        Payment? result = await this.service.FindPayment(TestData.MerchantId, TestData.PaymentId, TestContext.Current.CancellationToken);

        Assert.Same(stored, result);
    }

    [Fact]
    public async Task FindPayment_AnotherMerchantsPayment_ReturnsNull()
    {
        this.paymentRepo
            .Setup(r => r.Find(TestData.PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.Payment(PaymentStatus.Authorized));

        Payment? result = await this.service.FindPayment("merchant-b", TestData.PaymentId, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
