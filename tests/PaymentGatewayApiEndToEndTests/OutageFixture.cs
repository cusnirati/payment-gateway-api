using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaymentGatewayApiEndToEndTests;

// the real API with DynamoDB and the bank both unreachable: their addresses point at a local port nothing listens on
public class OutageFixture : WebApplicationFactory<Program>
{
    private readonly string unreachableUrl = $"http://127.0.0.1:{GetClosedPort()}";

    public OutageFixture()
    {
        ApiFixture.UseDummyAwsCredentials();
    }

    // UseSetting reaches Program.cs before it reads the AWS options, ConfigureAppConfiguration would come too late
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AWS:ServiceURL", this.unreachableUrl);
        builder.UseSetting("SimulatorBankBaseAddress", this.unreachableUrl);
    }

    private static int GetClosedPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
