using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
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
    private readonly GatewayOptions options;

    public PaymentDetailsValidator(ILogger<PaymentDetailsValidator> logger, IOptions<GatewayOptions> options)
    {
        this.logger = logger;
        this.options = options.Value;
    }

    public ValidationError? ValidateAmount(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsAmountValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Amount),
                ErrorMessage = "Amount must be a whole number of minor currency units, at least 1."
            };
        }

        return null;
    }

    public ValidationError? ValidateCurrencyCode(PaymentRequest payment)
    {
        if (!this.options.SupportedCurrencies.Contains(payment.Currency))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCurrencyCodeValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Currency),
                ErrorMessage = $"Currency '{payment.Currency}' is not supported"
            };
        }

        return null;
    }
}
