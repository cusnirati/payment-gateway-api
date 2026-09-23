using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGatewayApi.Security;

// authenticates a merchant from its X-Api-Key header; the merchant id becomes a claim that scopes
// every payment the request creates or reads
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly IMerchantApiKeys merchantApiKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IMerchantApiKeys merchantApiKeys)
        : base(options, logger, encoder)
    {
        this.merchantApiKeys = merchantApiKeys;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string apiKey = this.Request.Headers[HeaderName].ToString();
        if (apiKey.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? merchantId = this.merchantApiKeys.FindMerchant(apiKey);
        if (merchantId is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
        }

        ClaimsIdentity identity = new([new Claim(MerchantClaims.MerchantId, merchantId)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        this.Response.Headers.WWWAuthenticate = SchemeName;
        return base.HandleChallengeAsync(properties);
    }
}
