using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.Tests;

public sealed class PlcClientCapabilityCatalogTests
{
    [Fact]
    public void ForProtocol_ClassifiesTcpProtocolsAsNativeIo()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.ModbusTcp);

        Assert.Equal(PlcClientAsyncKind.NativeIo, capabilities.AsyncKind);
        Assert.True(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsBatchRead);
        Assert.True(capabilities.RequiresSerializedAccess);
        Assert.False(capabilities.SupportsConcurrentRequests);
    }

    [Fact]
    public void ForProtocol_ClassifiesSerialProtocolsAsDedicatedThread()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.ModbusRtu);

        Assert.Equal(PlcClientAsyncKind.DedicatedThread, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsBatchRead);
        Assert.True(capabilities.RequiresSerializedAccess);
    }

    [Fact]
    public void ForProtocol_ClassifiesVirtualPlcAsSynchronousCompletion()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.VirtualPlc);

        Assert.Equal(PlcClientAsyncKind.SynchronousCompletion, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.False(capabilities.SupportsBatchRead);
        Assert.True(capabilities.SupportsConcurrentRequests);
        Assert.False(capabilities.RequiresSerializedAccess);
    }

    [Fact]
    public void ForProtocol_ClassifiesOpcUaAsSubscriptionPreferred()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.OpcUa);

        Assert.Equal(PlcClientAsyncKind.DedicatedThread, capabilities.AsyncKind);
        Assert.True(capabilities.SupportsBatchRead);
        Assert.True(capabilities.SupportsSubscription);
        Assert.True(capabilities.RequiresSerializedAccess);
    }

    [Fact]
    public void Normalize_UsesProtocolDefaultWhenCapabilitiesAreMissing()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.Normalize(null, PlcProtocol.Plugin);

        Assert.Equal(PlcClientAsyncKind.SyncOnly, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.RequiresSerializedAccess);
        Assert.NotNull(capabilities.Notes);
    }

    [Fact]
    public void GetRegisteredDrivers_IncludesCapabilities()
    {
        PlcDriverPluginInfo modbus = PlcDriverPluginRegistry.GetRegisteredDrivers()
            .First(driver => driver.DriverId == "builtin.modbus-tcp");

        Assert.NotNull(modbus.Capabilities);
        Assert.Equal(PlcClientAsyncKind.NativeIo, modbus.Capabilities.AsyncKind);
        Assert.True(modbus.Capabilities.SupportsBatchRead);
    }
}
