namespace PaymentGatewayApi.Models;

public enum PaymentStatus
{
    Authorized, // payment was authorized by the acquiring bank
    Declined, // payment was declined by the acquiring bank
    Rejected, //  payment could not be completed as invalid information was supplied to the payment gateway
    InternalError, // payment could not be completed due to an internal error. Should never happen
    Pending // payment 
}
