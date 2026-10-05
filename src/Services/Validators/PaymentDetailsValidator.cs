using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public interface IPaymentDetailsValidator
{
    ValidationError? ValidateAmount(PaymentRequest payment);
    ValidationError? ValidateCurrencyCode(PaymentRequest payment);
}

public class PaymentDetailsValidator : IPaymentDetailsValidator
{
    private readonly ILogger<PaymentDetailsValidator> logger;
    private readonly IConfiguration config;

    public PaymentDetailsValidator(ILogger<PaymentDetailsValidator> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public ValidationError? ValidateAmount(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsAmountValid", false);

            return ValidationError.InvalidAmount;
        }

        return null;
    }

    public ValidationError? ValidateCurrencyCode(PaymentRequest payment)
    {
        string[] supportedCurrencies = this.config.GetSection("SupportedCurrencies").Get<string[]>() ?? [];
        if (!supportedCurrencies.Contains(payment.Currency, StringComparer.OrdinalIgnoreCase))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCurrencyCodeValid", false);

            return ValidationError.InvalidCurrencyCode;
        }

        return null;
    }
}
