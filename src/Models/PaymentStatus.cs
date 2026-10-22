using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Authorized = 1, // payment was authorized by the acquiring bank
    Declined = 2, // payment was declined by the acquiring bank
    Unavailable = 3 // payment is valid, but the acquiring bank could not be reached or failed, even after one retry
}
