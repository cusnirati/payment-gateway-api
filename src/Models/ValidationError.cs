namespace PaymentGatewayApi.Models;

// no RFC 9457 for simplicity. https://www.rfc-editor.org/info/rfc9457/
public class ValidationError
{
    public required ValidationErrorCode ErrorCode { get; set; }
    public required string ErrorMessage { get; set; }
}
