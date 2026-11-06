using System;
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
    ValidationError? ValidateCardNumberByLuhnCheck(PaymentRequest payment);
}

public class CardDetailsValidator : ICardDetailsValidator
{
    // 14 to 19 ASCII digits
    private static readonly Regex CardNumberRegex = new(@"^[0-9]{14,19}\z", RegexOptions.Compiled);
    // 3 to 4 ASCII digits
    private static readonly Regex CvvRegex = new(@"^[0-9]{3,4}\z", RegexOptions.Compiled);

    private readonly ILogger<CardDetailsValidator> logger;
    private readonly TimeProvider timeProvider;

    public CardDetailsValidator(ILogger<CardDetailsValidator> logger, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    public ValidationError? ValidateCardNumber(PaymentRequest payment)
    {
        if (!CardNumberRegex.IsMatch(payment.CardNumber))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.CardNumber),
                ErrorMessage = "Card number must be a string of 14 to 19 ASCII digits."
            };
        }

        return null;
    }

    // https://stripe.com/gb/resources/more/how-to-use-the-luhn-algorithm-a-guide-in-applications-for-businesses
    public ValidationError? ValidateCardNumberByLuhnCheck(PaymentRequest payment)
    {
        int sum = 0;
        bool doubleDigit = false;
        for (int i = payment.CardNumber.Length - 1; i >= 0; i--)
        {
            int digit = payment.CardNumber[i] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        if (sum % 10 != 0)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberLuhnValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.CardNumber),
                ErrorMessage = "Card number failed the Luhn checksum."
            };
        }

        return null;
    }

    public ValidationError? ValidateCardExpiry(PaymentRequest payment)
    {
        if (payment.ExpiryMonth < 1 || payment.ExpiryMonth > 12)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryMonthValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.ExpiryMonth),
                ErrorMessage = $"Expiry month '{payment.ExpiryMonth}' is not valid. Expiry month must be 1 to 12."
            };
        }

        if (payment.ExpiryYear < 1000 || payment.ExpiryYear > 9999)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryYearValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.ExpiryYear),
                ErrorMessage = $"Expiry year '{payment.ExpiryYear}' is not valid. Expiry year must be a four-digit year."
            };
        }

        string expiryStr = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
        DateOnly expiry = new(payment.ExpiryYear, payment.ExpiryMonth, 1);
        DateTimeOffset nowDt = this.timeProvider.GetUtcNow();
        DateOnly now = new(nowDt.Year, nowDt.Month, 1);

        if (expiry < now)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.ExpiryYear),
                ErrorMessage = $"Card expired. Expiry date '{expiryStr}' is before the current month."
            };
        }

        return null;
    }

    public ValidationError? ValidateCardCvv(PaymentRequest payment)
    {
        if (!CvvRegex.IsMatch(payment.Cvv))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCvvValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Cvv),
                ErrorMessage = "CVV must be a string of 3 or 4 ASCII digits."
            };
        }

        return null;
    }
}
