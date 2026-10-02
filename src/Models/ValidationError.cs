using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

// no RFC 9457 for simplicity. https://www.rfc-editor.org/info/rfc9457/
[JsonConverter(typeof(JsonStringEnumConverter<ValidationError>))]
public enum ValidationError
{
    InvalidCardNumber,
    InvalidDateFormatExpiry,
    CardExpired,
    InvalidAmount,
    InvalidCurrencyCode,
    InvalidCvv
}
