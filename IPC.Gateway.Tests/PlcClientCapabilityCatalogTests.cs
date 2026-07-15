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
        Assert.Equal(PlcPreferredReadMode.Batch, capabilities.PreferredReadMode);
        Assert.Equal(256, capabilities.MaxBatchItems);
        Assert.True(capabilities.SupportsAddressValidation);
        Assert.True(capabilities.RequiresSerializedAccess);
        Assert.False(capabilities.SupportsConcurrentRequests);
    }

    [Theory]
    [InlineData(PlcProtocol.ModbusRtu)]
    [InlineData(PlcProtocol.ModbusAscii)]
    public void ForProtocol_ClassifiesSerialProtocolsAsDedicatedThread(PlcProtocol protocol)
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(protocol);

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
        Assert.Equal(PlcPreferredReadMode.Subscription, capabilities.PreferredReadMode);
        Assert.Equal(1000, capabilities.MaxSubscriptionItems);
        Assert.True(capabilities.RequiresSerializedAccess);
    }

    [Fact]
    public void ForProtocol_ClassifiesMeterProtocolsAsReadOnly()
    {
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.Dlt6452007);

        Assert.True(capabilities.SupportsRead);
        Assert.False(capabilities.SupportsWrite);
        Assert.Equal(64, capabilities.MaxBatchItems);
    }

    [Fact]
    public void GetCapabilities_RemovesCapabilitiesMissingFromClientInterfaces()
    {
        using MisreportingClient client = new MisreportingClient();

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);

        Assert.Equal(PlcClientAsyncKind.DedicatedThread, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.False(capabilities.SupportsBatchRead);
        Assert.False(capabilities.SupportsSubscription);
        Assert.Equal(PlcPreferredReadMode.Single, capabilities.PreferredReadMode);
        Assert.Equal(0, capabilities.MaxBatchItems);
    }

    [Theory]
    [InlineData("40001", true)]
    [InlineData("not-a-modbus-address", false)]
    public void DriverAddressValidation_UsesProtocolParser(string address, bool expectedValid)
    {
        ModbusTcpProtocolDriver driver = new ModbusTcpProtocolDriver();

        PlcTagValidationResult result = driver.ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.ModbusTcp },
            address,
            PlcDataType.Int16,
            1,
            0);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void RegistryValidation_UsesExplicitDeviceProtocolDuringConfigurationMigration()
    {
        PlcConnectionOptions staleConnection = new PlcConnectionOptions
        {
            Protocol = PlcProtocol.RockwellCip
        };

        bool found = PlcDriverPluginRegistry.TryValidateTag(
            staleConnection,
            PlcProtocol.ModbusTcp,
            "HR0",
            PlcDataType.Int16,
            1,
            0,
            out PlcTagValidationResult result);

        Assert.True(found);
        Assert.True(result.IsValid);
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

    private sealed class MisreportingClient : IPlcClient, IPlcClientCapabilityProvider
    {
        public bool IsConnected => false;
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public PlcClientCapabilities GetCapabilities() => new PlcClientCapabilities
        {
            AsyncKind = PlcClientAsyncKind.NativeIo,
            PreferredReadMode = PlcPreferredReadMode.Subscription,
            SupportsNativeAsync = true,
            SupportsBatchRead = true,
            SupportsSubscription = true,
            MaxBatchItems = 50,
            MaxSubscriptionItems = 100
        };

        public void Connect() { }
        public void Disconnect() { }
        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset) =>
            throw new NotSupportedException();
        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset) =>
            throw new NotSupportedException();
        public void Dispose() { }
    }
}
