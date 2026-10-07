using System.Text.Json.Serialization;

namespace PaymentGatewayApi.Models.Responses;

// example payload
//   "authorized": true,
//   "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
public class BankResponse
{
    public bool Authorized { get; set; }

    [JsonPropertyName("authorization_code")]
    public string? AuthorizationCode { get; set; }
}
