using System;
using System.Text.RegularExpressions;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Services;

public interface IPaymentRequestValidator
{
    bool IsCardNumberValid(PaymentRequest payment);
    bool IsExpiryValid(PaymentRequest payment, DateTime now);
    bool IsAmountValid(PaymentRequest payment);
    bool IsCurrencyCodeValid(PaymentRequest payment);
    bool IsCvvValid(PaymentRequest payment);
}

// todo add logs for invalid
public class PaymentRequestValidator : IPaymentRequestValidator
{
    private readonly ILogger<PaymentRequestValidator> logger;

    public PaymentRequestValidator(ILogger<PaymentRequestValidator> logger)
    {
        this.logger = logger;
    }

    public bool IsCardNumberValid(PaymentRequest payment)
    {
        if (!Regex.IsMatch(payment.CardNumber, @"^\d{14,19}$"))
        {
            this.logger.LogInformation("Validator.IsCardNumberValid, PaymentId={PaymentId}, IsValid={IsValid}", payment.PaymentId, false);
            return false;
        }

        return true;
    }

    public bool IsExpiryValid(PaymentRequest payment, DateTime now)
    {
        // most probably not needed, date should be already valid
        if (payment.ExpiryMonth < 1 || payment.ExpiryMonth > 12)
        {
            this.logger.LogInformation(
                "Validator.IsExpiryValid, PaymentId={PaymentId}, IsValid={IsValid}, Reason={Reason}",
                payment.PaymentId, false, "Month out of range");

            return false;
        }

        var expiry = new DateTime(payment.ExpiryYear, payment.ExpiryMonth, 1);
        if (expiry < new DateTime(now.Year, now.Month, 1))
        {
            this.logger.LogInformation(
                "Validator.IsExpiryValid, PaymentId={PaymentId}, IsValid={IsValid}, Reason={Reason}",
                payment.PaymentId, false, "Card expired");

            return false;
        }

        return true;
    }

    public bool IsAmountValid(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "Validator.IsAmountValid, PaymentId={PaymentId}, IsValid={IsValid}, Reason={Reason}",
                payment.PaymentId, false, "Invalid amount");

            return false;
        }

        return true;
    }

    public bool IsCurrencyCodeValid(PaymentRequest payment)
    {
        // should be a list of accepted currencies, via database, config
        if (payment.Currency.Length != 3)
        {
            this.logger.LogInformation(
                "Validator.IsCurrencyCodeValid, PaymentId={PaymentId}, IsValid={IsValid}, Reason={Reason}",
                payment.PaymentId, false, "Invalid currency");

            return false;
        }

        return true;
    }

    public bool IsCvvValid(PaymentRequest payment)
    {
        // 3 or 4 digits
        if (!Regex.IsMatch(payment.Cvv, @"^\d{3,4}$"))
        {
            this.logger.LogInformation(
                "Validator.IsCvvValid, PaymentId={PaymentId}, IsValid={IsValid}, Reason={Reason}",
                payment.PaymentId, false, "Invalid CVV");

            return false;
        }

        return true;
    }
}
