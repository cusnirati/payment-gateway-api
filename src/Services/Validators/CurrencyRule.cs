using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class CurrencyRule : IPaymentRule
{
    private readonly ILogger<CurrencyRule> logger;
    private readonly GatewayOptions options;

    public CurrencyRule(ILogger<CurrencyRule> logger, IOptions<GatewayOptions> options)
    {
        this.logger = logger;
        this.options = options.Value;
    }

    public ValidationError? Validate(PaymentRequest payment)
    {
        if (!this.options.SupportedCurrencies.Contains(payment.Currency))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsCurrencyCodeValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Currency),
                ErrorMessage = $"Currency '{payment.Currency}' is not supported."
            };
        }

        return null;
    }
}
