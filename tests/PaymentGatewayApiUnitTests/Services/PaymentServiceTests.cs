using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PaymentGatewayApi.Configuration;
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
            NullLogger<PaymentService>.Instance,
            this.bankClient.Object,
            this.paymentRepo.Object,
            this.timeProvider,
            Options.Create(new PaymentOptions { SupportedCurrencies = ["GBP"], IdempotencyKeyRetention = TimeSpan.FromHours(24) }));
    }

    private async Task<Payment> Process(PaymentRequest request, CancellationToken cancellationToken)
    {
        PaymentAttempt attempt = await this.service.ProcessPayment(TestData.MerchantId, request, null, cancellationToken);
        return Assert.IsType<PaymentAttempt.Processed>(attempt).Payment;
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

        await this.Process(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

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

        Payment result = await this.Process(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

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
            () => this.Process(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken));

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

        Payment result = await this.Process(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task ProcessPayment_CancelledBeforeBankCall_ThrowsWithoutCallingBank()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.Process(TestData.ValidPaymentRequest(), cancelled.Token));

        this.bankClient.Verify(
            c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_BankCallIgnoresTheRequestToken()
    {
        this.SetupBank(BankOutcome.Declined);

        await this.Process(TestData.ValidPaymentRequest(), TestContext.Current.CancellationToken);

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

    private void SetupIdempotencyKeyTaken(PaymentRequest firstRequest, Payment? firstPayment)
    {
        this.paymentRepo
            .Setup(r => r.Add(It.IsAny<Payment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        this.paymentRepo
            .Setup(r => r.FindIdempotencyRecord(TestData.MerchantId, "order-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyRecord
            {
                MerchantId = TestData.MerchantId,
                Key = "order-42",
                PaymentId = TestData.PaymentId,
                RequestFingerprint = IdempotencyRecord.Fingerprint(firstRequest),
                CreatedAt = TestData.Now,
                ExpiresAt = TestData.Now.AddDays(1)
            });
        this.paymentRepo.Setup(r => r.Find(TestData.PaymentId, It.IsAny<CancellationToken>())).ReturnsAsync(firstPayment);
    }

    private void VerifyBankNeverCalled()
    {
        this.bankClient.Verify(
            c => c.Authorize(It.IsAny<Guid>(), It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_NewIdempotencyKey_StoresItWithThePaymentAndProcesses()
    {
        IdempotencyRecord? stored = null;
        Payment? added = null;
        this.paymentRepo
            .Setup(r => r.Add(It.IsAny<Payment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, IdempotencyRecord, CancellationToken>((payment, record, _) =>
            {
                added = payment;
                stored = record;
            })
            .ReturnsAsync(true);
        this.SetupBank(BankOutcome.Authorized("auth-code"));

        PaymentAttempt attempt = await this.service.ProcessPayment(
            TestData.MerchantId, TestData.ValidPaymentRequest(), "order-42", TestContext.Current.CancellationToken);

        Payment payment = Assert.IsType<PaymentAttempt.Processed>(attempt).Payment;
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.NotNull(stored);
        Assert.NotNull(added);
        Assert.Equal("order-42", stored.Key);
        Assert.Equal(TestData.MerchantId, stored.MerchantId);
        Assert.Equal(added.Id, stored.PaymentId);
        Assert.Equal(TestData.Now.AddHours(24), stored.ExpiresAt);
        this.paymentRepo.Verify(r => r.Add(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_RepeatedKeyForACompletedPayment_ReplaysItWithoutCallingBank()
    {
        PaymentRequest request = TestData.ValidPaymentRequest();
        Payment first = TestData.Payment(PaymentStatus.Declined);
        this.SetupIdempotencyKeyTaken(request, first);

        PaymentAttempt attempt = await this.service.ProcessPayment(
            TestData.MerchantId, request, "order-42", TestContext.Current.CancellationToken);

        Assert.Same(first, Assert.IsType<PaymentAttempt.Replayed>(attempt).Payment);
        this.VerifyBankNeverCalled();
    }

    [Fact]
    public async Task ProcessPayment_RepeatedKeyWhileTheFirstIsPending_IsInProgress()
    {
        PaymentRequest request = TestData.ValidPaymentRequest();
        this.SetupIdempotencyKeyTaken(request, TestData.Payment(PaymentStatus.Pending));

        PaymentAttempt attempt = await this.service.ProcessPayment(
            TestData.MerchantId, request, "order-42", TestContext.Current.CancellationToken);

        Assert.Equal(TestData.PaymentId, Assert.IsType<PaymentAttempt.InProgress>(attempt).PaymentId);
        this.VerifyBankNeverCalled();
    }

    [Fact]
    public async Task ProcessPayment_RepeatedKeyForADifferentRequest_IsKeyReused()
    {
        this.SetupIdempotencyKeyTaken(TestData.ValidPaymentRequest(), TestData.Payment(PaymentStatus.Authorized));

        PaymentAttempt attempt = await this.service.ProcessPayment(
            TestData.MerchantId, TestData.ValidPaymentRequest() with { Amount = 1 }, "order-42", TestContext.Current.CancellationToken);

        Assert.IsType<PaymentAttempt.KeyReused>(attempt);
        this.VerifyBankNeverCalled();
    }
}
