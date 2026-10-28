using System;
using Microsoft.Extensions.Options;

namespace PaymentGatewayApi.Services;

public class BankOptionsValidator : IValidateOptions<BankOptions>
{
    public ValidateOptionsResult Validate(string? name, BankOptions options)
    {
        // on Unix "/payments" parses as an absolute file:// uri, hence the scheme check
        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out Uri? bankUri)
            || (bankUri.Scheme != Uri.UriSchemeHttp && bankUri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail(
                $"SimulatorBankBaseAddress must be an absolute http or https url, got '{options.BaseAddress}'");
        }

        return ValidateOptionsResult.Success;
    }
}
