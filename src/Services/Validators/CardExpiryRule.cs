using System;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class CardExpiryRule : IPaymentRule
{
    private readonly ILogger<CardExpiryRule> logger;
    private readonly TimeProvider timeProvider;

    public CardExpiryRule(ILogger<CardExpiryRule> logger, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    public ValidationError? Validate(PaymentRequest payment)
    {
        if (payment.ExpiryMonth < 1 || payment.ExpiryMonth > 12)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsExpiryMonthValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.ExpiryMonth),
                ErrorMessage = $"Expiry month '{payment.ExpiryMonth}' is not valid. Expiry month must be 1 to 12."
            };
        }

        if (payment.ExpiryYear < 1000 || payment.ExpiryYear > 9999)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsExpiryYearValid", "Invalid");

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
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsExpiryValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.ExpiryYear),
                ErrorMessage = $"Card expired. Expiry date '{expiryStr}' is before the current month."
            };
        }

        return null;
    }
}
