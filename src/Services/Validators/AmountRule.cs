using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class AmountRule : IPaymentRule
{
    private readonly ILogger<AmountRule> logger;

    public AmountRule(ILogger<AmountRule> logger)
    {
        this.logger = logger;
    }

    public ValidationError? Validate(PaymentRequest payment)
    {
        if (payment.Amount < 1)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsAmountValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Amount),
                ErrorMessage = "Amount must be a whole number of minor currency units, at least 1."
            };
        }

        return null;
    }
}
