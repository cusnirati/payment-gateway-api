using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services.Validators;

public interface IValidationOrchestrator
{
    List<ValidationError?> ValidatePayment(PaymentRequest payment);
}

public class ValidationOrchestrator : IValidationOrchestrator
{
    private readonly ICardDetailsValidator cardValidator;
    private readonly IPaymentDetailsValidator paymentValidator;

    public ValidationOrchestrator(ICardDetailsValidator cardVlidator, IPaymentDetailsValidator paymentValidator)
    {
        this.cardValidator = cardVlidator;
        this.paymentValidator = paymentValidator;
    }

    // 
    public List<ValidationError?> ValidatePayment(PaymentRequest payment)
    {
        var validationErrors = new List<ValidationError?>();

        validationErrors.Add(this.cardValidator.IsCardNumberValid(payment));
        validationErrors.Add(this.cardValidator.IsExpiryValid(payment, DateTime.UtcNow));
        validationErrors.Add(this.cardValidator.IsCvvValid(payment));

        validationErrors.Add(this.paymentValidator.IsAmountValid(payment));
        validationErrors.Add(this.paymentValidator.IsCurrencyCodeValid(payment));

        validationErrors.RemoveAll(x => x == null);

        return validationErrors;
    }
}
