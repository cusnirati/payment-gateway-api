using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ValidationErrorCode>))]
public enum ValidationErrorCode
{
    InvalidCardNumber,
    InvalidDateFormatExpiry,
    CardExpired,
    InvalidAmount,
    InvalidCurrencyCode,
    InvalidCvv
}
