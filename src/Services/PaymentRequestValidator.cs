using System;
using System.Linq;
using System.Text.RegularExpressions;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Configuration;
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
    private readonly ILogger<PaymentRequestValidator> logger;
    private readonly IConfiguration config;

    public PaymentRequestValidator(ILogger<PaymentRequestValidator> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public bool IsCardNumberValid(PaymentRequest payment)
    {
        if (!Regex.IsMatch(payment.CardNumber, @"^\d{14,19}$"))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberValid", false);
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
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryMonthValid", false);

            return false;
        }

        // compared as numbers, not as a DateTime, so an out of range year is rejected instead of throwing;
        // the bank expects a four digit year
        bool isExpired = payment.ExpiryYear < now.Year || (payment.ExpiryYear == now.Year && payment.ExpiryMonth < now.Month);
        if (isExpired || payment.ExpiryYear > 9999)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryValid", false);

            return false;
        }

        return true;
    }

    public bool IsAmountValid(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsAmountValid", false);

            return false;
        }

        return true;
    }

    public bool IsCurrencyCodeValid(PaymentRequest payment)
    {
        string[] supportedCurrencies = this.config.GetSection("SupportedCurrencies").Get<string[]>() ?? [];
        if (!supportedCurrencies.Contains(payment.Currency, StringComparer.OrdinalIgnoreCase))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCurrencyCodeValid", false);

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
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCvvValid", false);

            return false;
        }

        return true;
    }
}
