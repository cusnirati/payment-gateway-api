using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Security;
using Xunit;

namespace PaymentGatewayApiUnitTests.Security;

public class MerchantApiKeysTests
{
    private readonly MerchantApiKeys keys = new(Options.Create(new ApiKeyOptions
    {
        Merchants =
        [
            new MerchantApiKey { MerchantId = "merchant-a", KeySha256 = MerchantApiKeys.Hash("key-a") },
            new MerchantApiKey { MerchantId = "merchant-b", KeySha256 = MerchantApiKeys.Hash("key-b") }
        ]
    }));

    [Fact]
    public void Hash_IsLowerCaseHexSha256()
    {
        // printf %s "dev-key-merchant-a" | sha256sum
        Assert.Equal(
            "c8c3a5a56d9ba1bff80c60a7d11478525838e0bef3d728fc4185aa3da1b84015", MerchantApiKeys.Hash("dev-key-merchant-a"));
    }

    [Theory]
    [InlineData("key-a", "merchant-a")]
    [InlineData("key-b", "merchant-b")]
    public void FindMerchant_KnownKey_ReturnsItsMerchant(string apiKey, string merchantId)
    {
        Assert.Equal(merchantId, this.keys.FindMerchant(apiKey));
    }

    [Theory]
    [InlineData("key-c")]
    [InlineData("KEY-A")]
    [InlineData("")]
    public void FindMerchant_UnknownKey_ReturnsNull(string apiKey)
    {
        Assert.Null(this.keys.FindMerchant(apiKey));
    }
}
