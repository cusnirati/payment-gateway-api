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

public class PaymentRequestValidator : IPaymentRequestValidator
{
    // cards are issued with an expiry a few years out, anything further is a typo or garbage
    private const int MaxExpiryYearsAhead = 20;

    private readonly ILogger<PaymentRequestValidator> logger;

    public PaymentRequestValidator(ILogger<PaymentRequestValidator> logger)
    {
        this.logger = logger;
    }

    public bool IsCardNumberValid(PaymentRequest payment)
    {
        if (!Regex.IsMatch(payment.CardNumber, @"^\d{14,19}$"))
        {
            this.logger.LogInformation(
                "Validator.IsCardNumberValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);
            return false;
        }

        return true;
    }

    public bool IsExpiryValid(PaymentRequest payment, DateTime now)
    {
        if (payment.ExpiryMonth < 1 || payment.ExpiryMonth > 12)
        {
            this.logger.LogInformation(
                "Validator.IsExpiryMonthValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);

            return false;
        }

        // compare year and month directly, building a DateTime would throw for an out of range year
        bool isExpired = payment.ExpiryYear < now.Year || (payment.ExpiryYear == now.Year && payment.ExpiryMonth < now.Month);
        bool isTooFarAhead = payment.ExpiryYear > now.Year + MaxExpiryYearsAhead;
        if (isExpired || isTooFarAhead)
        {
            this.logger.LogInformation(
                "Validator.IsExpiryValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);

            return false;
        }

        return true;
    }

    public bool IsAmountValid(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "Validator.IsAmountValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);

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
                "Validator.IsCurrencyCodeValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);

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
                "Validator.IsCvvValid, PaymentId={PaymentId}, IsValid={IsValid}",
                payment.PaymentId, false);

            return false;
        }

        return true;
    }
}
