using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;

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
        // [0-9] rather than \d, which also matches non-ASCII digits
        if (!Regex.IsMatch(payment.CardNumber, @"^[0-9]{14,19}$"))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberValid", false);
            return false;
        }

        return true;
    }

    public bool IsExpiryValid(PaymentRequest payment, DateTime nowDt)
    {
        string expiryStr = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}";
        if (!DateTime.TryParseExact(expiryStr, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiryDt))
        {
            this.logger.LogWarning(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsExpiryDateFormatValid", false);

            return false;
        }

        DateOnly expiry = new(expiryDt.Year, expiryDt.Month, 1);
        DateOnly now = new(nowDt.Year, nowDt.Month, 1);

        if (expiry < now)
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
