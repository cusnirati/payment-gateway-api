using System;
using System.Linq;
using Microsoft.Extensions.Options;

namespace PaymentGatewayApi.Services;

public class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        // on Unix "/payments" parses as an absolute file:// uri, hence the scheme check
        if (!Uri.TryCreate(options.BankBaseAddress, UriKind.Absolute, out Uri? bankUri)
            || (bankUri.Scheme != Uri.UriSchemeHttp && bankUri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail(
                $"SimulatorBankBaseAddress must be an absolute http or https url, got '{options.BankBaseAddress}'");
        }

        if (options.SupportedCurrencies.Length == 0)
        {
            return ValidateOptionsResult.Fail("SupportedCurrencies must list at least one currency");
        }

        if (options.SupportedCurrencies.Any(string.IsNullOrWhiteSpace))
        {
            return ValidateOptionsResult.Fail("SupportedCurrencies must not contain blank entries");
        }

        return ValidateOptionsResult.Success;
    }
}
