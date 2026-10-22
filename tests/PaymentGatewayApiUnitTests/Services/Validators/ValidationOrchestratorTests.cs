using System.Collections.Generic;
using Moq;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Services.Validators;
using Xunit;

namespace PaymentGatewayApiUnitTests.Services.Validators;

public class ValidationOrchestratorTests
{
    private readonly Mock<ICardDetailsValidator> cardValidator = new();
    private readonly Mock<IPaymentDetailsValidator> paymentValidator = new();
    private readonly ValidationOrchestrator orchestrator;
    private readonly PaymentRequest payment = TestPayments.Create();

    private readonly ValidationError cardNumberError = new() { Field = "cardNumber", ErrorMessage = "card number error" };
    private readonly ValidationError expiryError = new() { Field = "expiry", ErrorMessage = "expiry error" };
    private readonly ValidationError cvvError = new() { Field = "cvv", ErrorMessage = "cvv error" };
    private readonly ValidationError amountError = new() { Field = "amount", ErrorMessage = "amount error" };
    private readonly ValidationError currencyError = new() { Field = "currency", ErrorMessage = "currency error" };

    public ValidationOrchestratorTests()
    {
        this.orchestrator = new ValidationOrchestrator(this.cardValidator.Object, this.paymentValidator.Object);
    }

    [Fact]
    public void ValidatePayment_EveryValidatorPasses_ReturnsNoErrors()
    {
        List<ValidationError> errors = this.orchestrator.ValidatePayment(this.payment);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidatePayment_EveryValidatorFails_ReturnsEveryErrorInFieldOrder()
    {
        this.cardValidator.Setup(v => v.ValidateCardNumber(this.payment)).Returns(this.cardNumberError);
        this.cardValidator.Setup(v => v.ValidateCardExpiry(this.payment)).Returns(this.expiryError);
        this.cardValidator.Setup(v => v.ValidateCardCvv(this.payment)).Returns(this.cvvError);
        this.paymentValidator.Setup(v => v.ValidateAmount(this.payment)).Returns(this.amountError);
        this.paymentValidator.Setup(v => v.ValidateCurrencyCode(this.payment)).Returns(this.currencyError);

        List<ValidationError> errors = this.orchestrator.ValidatePayment(this.payment);

        Assert.Equal(
            new[] { this.cardNumberError, this.expiryError, this.cvvError, this.amountError, this.currencyError },
            errors);
    }

    [Fact]
    public void ValidatePayment_SomeValidatorsFail_ReturnsOnlyTheirErrors()
    {
        this.cardValidator.Setup(v => v.ValidateCardExpiry(this.payment)).Returns(this.expiryError);
        this.paymentValidator.Setup(v => v.ValidateCurrencyCode(this.payment)).Returns(this.currencyError);

        List<ValidationError> errors = this.orchestrator.ValidatePayment(this.payment);

        Assert.Equal(new[] { this.expiryError, this.currencyError }, errors);
    }

    [Fact]
    public void ValidatePayment_RunsEveryValidatorOnceOnThePayment()
    {
        this.orchestrator.ValidatePayment(this.payment);

        this.cardValidator.Verify(v => v.ValidateCardNumber(this.payment), Times.Once);
        this.cardValidator.Verify(v => v.ValidateCardExpiry(this.payment), Times.Once);
        this.cardValidator.Verify(v => v.ValidateCardCvv(this.payment), Times.Once);
        this.paymentValidator.Verify(v => v.ValidateAmount(this.payment), Times.Once);
        this.paymentValidator.Verify(v => v.ValidateCurrencyCode(this.payment), Times.Once);
        this.cardValidator.VerifyNoOtherCalls();
        this.paymentValidator.VerifyNoOtherCalls();
    }
}
