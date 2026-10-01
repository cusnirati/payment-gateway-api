using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGatewayApi.Controllers;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Services;
using Xunit;

namespace PaymentGatewayApiUnitTests.Controllers;

public class PaymentControllerTests
{
    private readonly Mock<IPaymentsRepository> paymentRepo = new();
    private readonly Mock<IPaymentRequestValidator> validator = new();
    private readonly Mock<IBankService> bankService = new();
    private readonly PaymentController controller;

    public PaymentControllerTests()
    {
        this.controller = new PaymentController(
            NullLogger<PaymentController>.Instance,
            this.paymentRepo.Object,
            this.validator.Object,
            this.bankService.Object);
    }

    private void SetupValidator()
    {
        this.validator.Setup(v => v.IsCardNumberValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsExpiryValid(It.IsAny<PaymentRequest>(), It.IsAny<DateTime>())).Returns(true);
        this.validator.Setup(v => v.IsAmountValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsCurrencyCodeValid(It.IsAny<PaymentRequest>())).Returns(true);
        this.validator.Setup(v => v.IsCvvValid(It.IsAny<PaymentRequest>())).Returns(true);
    }
}