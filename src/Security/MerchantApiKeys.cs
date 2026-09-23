using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;

namespace PaymentGatewayApi.Security;

public interface IMerchantApiKeys
{
    // the merchant the key belongs to, or null for an unknown key
    string? FindMerchant(string apiKey);
}

public class MerchantApiKeys : IMerchantApiKeys
{
    private readonly FrozenDictionary<string, string> merchantsByKeyHash;

    public MerchantApiKeys(IOptions<ApiKeyOptions> options)
    {
        this.merchantsByKeyHash = options.Value.Merchants.ToFrozenDictionary(
            merchant => merchant.KeySha256, merchant => merchant.MerchantId, StringComparer.Ordinal);
    }

    public static string Hash(string apiKey)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
    }

    public string? FindMerchant(string apiKey)
    {
        // the lookup compares hashes, so its timing says nothing about how much of a key was right
        return this.merchantsByKeyHash.GetValueOrDefault(Hash(apiKey));
    }
}
