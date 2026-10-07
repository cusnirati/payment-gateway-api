using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Rejected, // payment could not be completed as invalid information was supplied to the payment gateway
    Authorized, // payment was authorized by the acquiring bank
    Declined, // payment was declined by the acquiring bank
    Pending, // payment is valid, but was not authorized by the acquiring bank yet
    InternalServerError // an unexpected error had occurred
}
