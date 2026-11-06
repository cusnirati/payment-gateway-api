using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public class CardNumberLuhnRule : IPaymentRule
{
    private readonly ILogger<CardNumberLuhnRule> logger;

    public CardNumberLuhnRule(ILogger<CardNumberLuhnRule> logger)
    {
        this.logger = logger;
    }

    // https://stripe.com/gb/resources/more/how-to-use-the-luhn-algorithm-a-guide-in-applications-for-businesses
    public ValidationError? Validate(PaymentRequest payment)
    {
        int sum = 0;
        bool doubleDigit = false;
        for (int i = payment.CardNumber.Length - 1; i >= 0; i--)
        {
            int digit = payment.CardNumber[i] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        if (sum % 10 != 0)
        {
            this.logger.LogInformation(
                "PaymentId={PaymentId}, Operation={Operation}, IsValid={IsValid}",
                payment.PaymentId, "Validator.IsCardNumberLuhnValid", false);

            return new ValidationError
            {
                Field = nameof(PaymentRequest.CardNumber),
                ErrorMessage = "Card number failed the Luhn checksum."
            };
        }

        return null;
    }
}
