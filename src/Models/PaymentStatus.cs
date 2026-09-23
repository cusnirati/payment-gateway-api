namespace PaymentGatewayApi.Models;

// persisted by name, so members can be reordered without corrupting stored payments
public enum PaymentStatus
{
    Pending, // recorded, the bank has not answered yet
    Authorized, // the acquiring bank authorized the payment
    Declined, // the acquiring bank declined the payment
    Failed, // the bank refused the request or was unavailable, the card was not charged
    Unknown // no usable answer from the bank (timeout, unreadable body), the card may have been charged and needs reconciliation
}
