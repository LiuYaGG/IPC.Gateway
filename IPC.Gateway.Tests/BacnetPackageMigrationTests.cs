using IPC.Plc.Communication.Bacnet;
using IPC.Plc.Communication.Core;
using System.IO.BACnet;

namespace IPC.Gateway.Tests;

public sealed class BacnetPackageMigrationTests
{
    [Fact]
    public void Driver_LoadsElaCompilAssemblyAndStartsIpTransport()
    {
        Assert.Equal("BACnet", typeof(BacnetClient).Assembly.GetName().Name);
        Assert.Equal(new Version(3, 0, 2, 0), typeof(BacnetClient).Assembly.GetName().Version);

        using BacnetIpClient client = new(new PlcConnectionOptions
        {
            Host = "127.0.0.1",
            Port = 47808,
            TimeoutMilliseconds = 200,
            DriverOptionsJson = "{\"localPort\":0,\"retries\":0}"
        });

        client.Connect();
        Assert.True(client.IsConnected);

        client.Disconnect();
        Assert.False(client.IsConnected);
    }
}
