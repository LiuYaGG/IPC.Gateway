using System.Runtime.Loader;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.Tests;

public sealed class OpcUaPluginLoadTests
{
    [Fact]
    public void LegacyPlugin_LoadsOpcFoundationClientInIsolatedContext()
    {
        DirectoryInfo testBaseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = testBaseDirectory.Parent?.Name ?? "Debug";
        string pluginPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "IPC.Gateway.LegacyProtocolPlugins", "bin", configuration, "net10.0",
            "IPC.Gateway.LegacyProtocolPlugins.dll"));

        PlcDriverPluginLoadContext context = new PlcDriverPluginLoadContext(pluginPath);
        try
        {
            System.Reflection.Assembly assembly = context.LoadFromAssemblyPath(pluginPath);
            Type driverType = assembly.GetType(
                "IPC.Gateway.LegacyProtocolPlugins.OpcUaProtocolDriver",
                throwOnError: true)!;
            IProtocolDriver driver = Assert.IsAssignableFrom<IProtocolDriver>(Activator.CreateInstance(driverType));
            using IPlcClient client = driver.CreateClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.OpcUa,
                Host = "opc.tcp://localhost:4840"
            });

            Assert.Equal(PlcProtocol.OpcUa, client.Protocol);
            Assert.Equal("Opc.Ua.Client", client.GetType().GetField(
                    "_session",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .FieldType.Assembly.GetName().Name);
        }
        finally
        {
            context.Unload();
        }
    }
}
