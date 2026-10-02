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
    // 14 to 19 ASCII digits
    private readonly Regex cardNumberRegex = new(@"^[0-9]{14,19}$", RegexOptions.Compiled);

    // 3 or 4 ASCII digits
    private readonly Regex cvvRegex = new(@"^[0-9]{3,4}$", RegexOptions.Compiled);

    private readonly ILogger<CardDetailsValidator> logger;


    public CardDetailsValidator(ILogger<CardDetailsValidator> logger)
    {
        this.logger = logger;
    }

    public ValidationError? IsCardNumberValid(PaymentRequest payment)
    {
        if (!this.cardNumberRegex.IsMatch(payment.CardNumber))
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
        if (!this.cvvRegex.IsMatch(payment.Cvv))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCvvValid", false);

            return ValidationError.InvalidCvv;
        }

        return null;
    }
}
