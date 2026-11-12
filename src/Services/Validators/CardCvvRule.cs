using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class CardCvvRule : IPaymentRule
{
    // 3 to 4 ASCII digits
    private static readonly Regex CvvRegex = new(@"^[0-9]{3,4}\z", RegexOptions.Compiled);

    private readonly ILogger<CardCvvRule> logger;

    public CardCvvRule(ILogger<CardCvvRule> logger)
    {
        this.logger = logger;
    }

    public ValidationError? Validate(PaymentRequest payment)
    {
        if (!CvvRegex.IsMatch(payment.Cvv))
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, Status={Status}",
                payment.PaymentId, "Validator.IsCvvValid", "Invalid");

            return new ValidationError
            {
                Field = nameof(PaymentRequest.Cvv),
                ErrorMessage = "CVV must be a string of 3 or 4 ASCII digits."
            };
        }

        return null;
    }
}
