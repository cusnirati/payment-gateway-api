using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Rejected, // payment could not be completed as invalid information was supplied to the payment gateway
    Authorized, // payment was authorized by the acquiring bank
    Declined, // payment was declined by the acquiring bank
    Unavailable, // payment is valid, but the acquiring bank could not be reached or failed, even after one retry
    InternalServerError // an unexpected error had occurred
}
