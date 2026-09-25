namespace PaymentGatewayApi.Models;

public enum PaymentStatus
{
    Authorized, // the payment was authorized by the call to the acquiring bank
    Declined, // the payment was declined by the call to the acquiring bank
    Rejected, //  No payment could be created as invalid information was supplied to the payment gateway
    InternalError, // Payment could not be completed due to an internal error. Should never happen
    Pending // the payment was persisted before calling the acquiring bank, final status not yet known
}
