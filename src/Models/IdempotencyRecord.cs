using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PaymentGatewayApi.Models;

// ties a merchant's Idempotency-Key to the payment it created, so a retried request replays that
// payment instead of charging the card again
public sealed partial record IdempotencyRecord
{
    public const string HeaderName = "Idempotency-Key";

    public required string MerchantId { get; init; }
    public required string Key { get; init; }
    public required Guid PaymentId { get; init; }

    // tells a genuine retry from the same key reused for a different payment
    public required string RequestFingerprint { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // after this the key may be used again; DynamoDB's TTL deletes the record some time later
    public required DateTimeOffset ExpiresAt { get; init; }

    // visible ASCII, e.g. a GUID or the merchant's own order reference
    public static bool IsValidKey(string key)
    {
        return ValidKey().IsMatch(key);
    }

    // hashes what identifies the payment, except the full card number and the CVV: PCI DSS forbids
    // keeping the CVV in any form and allows the full number only under a keyed hash
    public static string Fingerprint(PaymentRequest request)
    {
        string identity = string.Join(
            '|',
            request.CardNumber[^4..],
            request.ExpiryMonth.ToString(CultureInfo.InvariantCulture),
            request.ExpiryYear.ToString(CultureInfo.InvariantCulture),
            request.Currency.ToUpperInvariant(),
            request.Amount.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    [GeneratedRegex("^[\\x21-\\x7E]{1,255}$")]
    private static partial Regex ValidKey();
}
