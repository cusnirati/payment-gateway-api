using Microsoft.Extensions.Configuration;

namespace PaymentGatewayApi.Configuration;

// represents IOptions<GatewayOptions>, being validated at startup
public class GatewayOptions
{
    [ConfigurationKeyName("SimulatorBankBaseAddress")]
    public string BankBaseAddress { get; set; } = string.Empty;

    public string[] SupportedCurrencies { get; set; } = [];
}
