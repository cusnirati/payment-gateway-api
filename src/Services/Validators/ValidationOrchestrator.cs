using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public interface IValidationOrchestrator
{
    List<ValidationError?> ValidatePayment(PaymentRequest payment);
}

public class ValidationOrchestrator : IValidationOrchestrator
{
    private readonly ICardDetailsValidator cardValidator;
    private readonly IPaymentDetailsValidator paymentValidator;

    public ValidationOrchestrator(ICardDetailsValidator cardValidator, IPaymentDetailsValidator paymentValidator)
    {
        this.cardValidator = cardValidator;
        this.paymentValidator = paymentValidator;
    }

    // leaving as is for simplicity, otherwise use something like https://github.com/FluentValidation/FluentValidation
    public List<ValidationError?> ValidatePayment(PaymentRequest payment)
    {
        var validationErrors = new List<ValidationError?>();

        validationErrors.Add(this.cardValidator.ValidateCardNumber(payment));
        validationErrors.Add(this.cardValidator.ValidateCardExpiry(payment));
        validationErrors.Add(this.cardValidator.ValidateCardCvv(payment));

        validationErrors.Add(this.paymentValidator.ValidateAmount(payment));
        validationErrors.Add(this.paymentValidator.ValidateCurrencyCode(payment));

        validationErrors.RemoveAll(x => x == null);

        return validationErrors;
    }
}
