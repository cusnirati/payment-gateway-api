namespace PaymentGatewayApi.Models;

// https://www.rfc-editor.org/info/rfc9457/
// one entry of the RFC 9457 validation problem: Field is the key under "errors", ErrorMessage one of its messages
public class ValidationError
{
    public required string Field { get; set; }
    public required string ErrorMessage { get; set; }
}
