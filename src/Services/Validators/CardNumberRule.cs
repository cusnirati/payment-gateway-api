using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class CardNumberRule : IPaymentRule
{
    // 14 to 19 ASCII digits
    private static readonly Regex CardNumberRegex = new(@"^[0-9]{14,19}\z", RegexOptions.Compiled);

    private readonly ILogger<CardNumberRule> logger;

    public CardNumberRule(ILogger<CardNumberRule> logger)
    {
        this.logger = logger;
    }

    public ValidationError? Validate(PaymentRequest payment)
    {
        if (!CardNumberRegex.IsMatch(payment.CardNumber))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsCardNumberValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.CardNumber),
                ErrorMessage = "Card number must be a string of 14 to 19 ASCII digits."
            };
        }

        return null;
    }
}
