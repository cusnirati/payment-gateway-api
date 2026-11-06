using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;

namespace PaymentGatewayApi.Services.Validators;

public interface IPaymentRule
{
    ValidationError? Validate(PaymentRequest payment);
}
