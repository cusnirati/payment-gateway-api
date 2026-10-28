using Microsoft.Extensions.Configuration;

namespace PaymentGatewayApi.Services;

public class BankOptions
{
    // kept at the configuration root under its original key, so appsettings, docker-compose and the tests stay as they are
    [ConfigurationKeyName("SimulatorBankBaseAddress")]
    public string BaseAddress { get; set; } = string.Empty;
}
