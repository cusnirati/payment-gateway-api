using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services.Validators;

public interface ICardDetailsValidator
{
    ValidationError? ValidateCardNumber(PaymentRequest payment);
    ValidationError? ValidateCardExpiry(PaymentRequest payment);
    ValidationError? ValidateCardCvv(PaymentRequest payment);
}

public class CardDetailsValidator : ICardDetailsValidator
{
    // 14 to 19 ASCII digits
    private readonly Regex cardNumberRegex = new(@"^[0-9]{14,19}$", RegexOptions.Compiled);

    // 3 or 4 ASCII digits
    private readonly Regex cvvRegex = new(@"^[0-9]{3,4}$", RegexOptions.Compiled);

    private readonly ILogger<CardDetailsValidator> logger;
    private readonly TimeProvider timeProvider;

    public CardDetailsValidator(ILogger<CardDetailsValidator> logger, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    public ValidationError? ValidateCardNumber(PaymentRequest payment)
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

    public ValidationError? ValidateCardExpiry(PaymentRequest payment)
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
        DateTimeOffset nowDt = this.timeProvider.GetUtcNow();
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

    public ValidationError? ValidateCardCvv(PaymentRequest payment)
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
