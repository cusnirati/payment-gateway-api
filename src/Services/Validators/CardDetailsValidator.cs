using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services.Validators;

public interface ICardDetailsValidator
{
    ValidationError? IsCardNumberValid(PaymentRequest payment);
    ValidationError? IsExpiryValid(PaymentRequest payment, DateTime now);
    ValidationError? IsCvvValid(PaymentRequest payment);
}

public class CardDetailsValidator : ICardDetailsValidator
{
    private readonly ILogger<CardDetailsValidator> logger;

    public CardDetailsValidator(ILogger<CardDetailsValidator> logger)
    {
        this.logger = logger;
    }

    public ValidationError? IsCardNumberValid(PaymentRequest payment)
    {
        // [0-9] rather than \d, which also matches non-ASCII digits
        if (!Regex.IsMatch(payment.CardNumber, @"^[0-9]{14,19}$"))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberValid", false);
            return ValidationError.InvalidCardNumber;
        }

        return null;
    }

    public ValidationError? IsExpiryValid(PaymentRequest payment, DateTime nowDt)
    {
        string expiryStr = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
        if (!DateTime.TryParseExact(expiryStr, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiryDt))
        {
            this.logger.LogWarning(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryDateFormatValid", false);

            return ValidationError.InvalidDateFormatExpiry;
        }

        DateOnly expiry = new(expiryDt.Year, expiryDt.Month, 1);
        DateOnly now = new(nowDt.Year, nowDt.Month, 1);

        if (expiry < now)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryValid", false);

            return ValidationError.CardExpired;
        }

        return null;
    }

    public ValidationError? IsCvvValid(PaymentRequest payment)
    {
        // 3 or 4 digits
        if (!Regex.IsMatch(payment.Cvv, @"^[0-9]{3,4}$"))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCvvValid", false);

            return ValidationError.InvalidCvv;
        }

        return null;
    }
}
