using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

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

    // not doing a Luhn check for simplicity
    public ValidationError? ValidateCardNumber(PaymentRequest payment)
    {
        if (!this.cardNumberRegex.IsMatch(payment.CardNumber))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberValid", false);
            return new ValidationError
            {
                ErrorCode = ValidationErrorCode.InvalidCardNumber,
                ErrorMessage = "Card number must be a string of 14 to 19 ASCII digits."
            };
        }

        return null;
    }

    public ValidationError? ValidateCardExpiry(PaymentRequest payment)
    {
        string expiryStr = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
        if (!DateTime.TryParseExact(expiryStr, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiryDt))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryDateFormatValid", false);

            return new ValidationError
            {
                ErrorCode = ValidationErrorCode.InvalidDateFormatExpiry,
                ErrorMessage = $"Expiry date '{expiryStr}' is not valid. Expiry month must be 1 to 12 and expiry year a four-digit year."
            };
        }

        DateOnly expiry = new(expiryDt.Year, expiryDt.Month, 1);
        DateTimeOffset nowDt = this.timeProvider.GetUtcNow();
        DateOnly now = new(nowDt.Year, nowDt.Month, 1);

        if (expiry < now)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryValid", false);

            return new ValidationError
            {
                ErrorCode = ValidationErrorCode.CardExpired,
                ErrorMessage = $"Card expired. Expiry date '{expiryStr}' is before the current month."
            };
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

            return new ValidationError
            {
                ErrorCode = ValidationErrorCode.InvalidCvv,
                ErrorMessage = "CVV must be a string of 3 or 4 ASCII digits."
            };
        }

        return null;
    }
}
