namespace PaymentGatewayApi.Models;

// what the acquiring bank said about one payment, in the gateway's own terms
public sealed record BankOutcome
{
    public static readonly BankOutcome Declined = new(PaymentStatus.Declined, null);
    public static readonly BankOutcome Failed = new(PaymentStatus.Failed, null);
    public static readonly BankOutcome Unknown = new(PaymentStatus.Unknown, null);

    private BankOutcome(PaymentStatus status, string? authorizationCode)
    {
        this.Status = status;
        this.AuthorizationCode = authorizationCode;
    }

    public PaymentStatus Status { get; }

    // needed later to capture, refund or dispute the payment
    public string? AuthorizationCode { get; }

    public static BankOutcome Authorized(string? authorizationCode)
    {
        return new BankOutcome(PaymentStatus.Authorized, authorizationCode);
    }
}
