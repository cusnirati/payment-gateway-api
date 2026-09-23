using System;
using System.Security.Claims;

namespace PaymentGatewayApi.Security;

public static class MerchantClaims
{
    public const string MerchantId = "merchant_id";

    public static string? FindMerchantId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(MerchantId);
    }

    // for endpoints behind [Authorize], where a missing claim is a wiring bug, not a client error
    public static string GetMerchantId(this ClaimsPrincipal user)
    {
        return user.FindMerchantId() ?? throw new InvalidOperationException("The request has no authenticated merchant.");
    }
}
