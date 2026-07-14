using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class P0RuntimeIsolationTests
{
    [Fact]
    public async Task SynchronousExecutor_UsesDedicatedWorkerThread()
    {
        using BoundedSynchronousIoExecutor executor = new BoundedSynchronousIoExecutor(1, 2);

        bool isThreadPoolThread = await executor.InvokeAsync(
            () => Thread.CurrentThread.IsThreadPoolThread,
            CancellationToken.None);

        Assert.False(isThreadPoolThread);
    }

    [Fact]
    public void PhysicalChannelManager_SharesSerialBusButNotIndependentTcpDevices()
    {
        DeviceConfig firstSerial = CreateDevice("serial-1", PlcProtocol.ModbusRtu, "COM3", 9600);
        DeviceConfig secondSerial = CreateDevice("serial-2", PlcProtocol.ModbusRtu, "com3", 9600);
        DeviceConfig firstTcp = CreateDevice("tcp-1", PlcProtocol.ModbusTcp, "10.0.0.8", 502);
        DeviceConfig secondTcp = CreateDevice("tcp-2", PlcProtocol.ModbusTcp, "10.0.0.8", 502);

        Assert.Equal(
            PhysicalChannelManager.BuildChannelKey(firstSerial),
            PhysicalChannelManager.BuildChannelKey(secondSerial));
        Assert.NotEqual(
            PhysicalChannelManager.BuildChannelKey(firstTcp),
            PhysicalChannelManager.BuildChannelKey(secondTcp));
    }

    [Fact]
    public void PhysicalChannelManager_SharesUdpEndpointAcrossDeviceConfigurations()
    {
        DeviceConfig first = CreateDevice("udp-1", PlcProtocol.MitsubishiMc, "10.0.0.8", 5000);
        DeviceConfig second = CreateDevice("udp-2", PlcProtocol.MitsubishiMc, "10.0.0.8", 5000);
        DeviceConfig otherPort = CreateDevice("udp-3", PlcProtocol.MitsubishiMc, "10.0.0.8", 5001);
        first.Connection.Transport = NetworkTransport.Udp;
        second.Connection.Transport = NetworkTransport.Udp;
        otherPort.Connection.Transport = NetworkTransport.Udp;

        Assert.Equal(
            PhysicalChannelManager.BuildChannelKey(first),
            PhysicalChannelManager.BuildChannelKey(second));
        Assert.NotEqual(
            PhysicalChannelManager.BuildChannelKey(first),
            PhysicalChannelManager.BuildChannelKey(otherPort));
    }

    [Fact]
    public async Task PhysicalChannelManager_MarksUdpChannelOfflineAfterThirdFailure()
    {
        DeviceConfig device = CreateDevice("udp", PlcProtocol.MitsubishiMc, "10.0.0.8", 5000);
        device.Connection.Transport = NetworkTransport.Udp;
        PhysicalChannelManager manager = new PhysicalChannelManager();

        using (PhysicalChannelLease first = await manager.AcquireAsync(device, CancellationToken.None))
            first.RecordFailure("timeout-1");
        Assert.Equal("Degraded", manager.GetSnapshot(device).Status);

        using (PhysicalChannelLease second = await manager.AcquireAsync(device, CancellationToken.None))
            second.RecordFailure("timeout-2");
        Assert.Equal("Degraded", manager.GetSnapshot(device).Status);

        using (PhysicalChannelLease third = await manager.AcquireAsync(device, CancellationToken.None))
            third.RecordFailure("timeout-3");
        Assert.Equal("Offline", manager.GetSnapshot(device).Status);
    }

    [Fact]
    public void RuntimeEngine_RetainsUdpConnectionUntilThirdTimeout()
    {
        DeviceConfig device = CreateDevice("udp", PlcProtocol.MitsubishiMc, "10.0.0.8", 5000);
        device.Connection.Transport = NetworkTransport.Udp;
        DeviceRuntimeState state = new DeviceRuntimeState(device, new CircuitBreakerOptions());

        Assert.True(RuntimeEngine.TryRegisterTransientUdpTimeout(state, "timeout-1"));
        Assert.Equal(1, state.ConsecutiveFailures);
        Assert.Equal("Degraded", state.StableStatus);
        Assert.False(state.IsIsolated);

        Assert.True(RuntimeEngine.TryRegisterTransientUdpTimeout(state, "timeout-2"));
        Assert.Equal(2, state.ConsecutiveFailures);
        Assert.Equal("Degraded", state.StableStatus);

        Assert.False(RuntimeEngine.TryRegisterTransientUdpTimeout(state, "timeout-3"));
        Assert.Equal(2, state.ConsecutiveFailures);
    }

    [Fact]
    public void CompiledReadPlan_IsolatesInvalidStaticAddress()
    {
        DeviceConfig device = CreateDevice("modbus", PlcProtocol.ModbusTcp, "127.0.0.1", 502);
        TagConfig tag = new TagConfig { Id = "bad", Name = "Bad", Address = "HR:not-a-number" };
        device.Tags.Add(tag);

        CompiledTagRead compiled = CompiledDeviceReadPlan.Compile(device).Get(tag);

        Assert.False(compiled.IsStaticallyValid);
        Assert.True(compiled.Runtime.IsIsolated);
        Assert.True(compiled.Runtime.IsStaticIsolation);
    }

    [Fact]
    public void TagRuntimeState_IsolatesAfterThreeFailuresAndRecoversOnSuccess()
    {
        TagRuntimeState state = new TagRuntimeState(false, string.Empty);

        state.RecordFailure("bad tag");
        state.RecordFailure("bad tag");
        Assert.False(state.IsIsolated);

        state.RecordFailure("bad tag");
        Assert.True(state.IsIsolated);
        Assert.True(state.NextRecoveryProbeUtc > DateTime.UtcNow);

        state.RecordSuccess();
        Assert.False(state.IsIsolated);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public void DeviceState_DoesNotStartOnline()
    {
        DeviceRuntimeState state = new DeviceRuntimeState(
            CreateDevice("device", PlcProtocol.VirtualPlc, string.Empty, 0),
            new CircuitBreakerOptions());

        Assert.Equal("Offline", state.StableStatus);
        Assert.Equal("Offline", state.DeviceState);
    }

    private static DeviceConfig CreateDevice(string id, PlcProtocol protocol, string host, int port)
    {
        return new DeviceConfig
        {
            Id = id,
            Name = id,
            Protocol = protocol,
            Connection = new PlcConnectionOptions
            {
                Protocol = protocol,
                Host = host,
                Port = port
            }
        };
    }
}
