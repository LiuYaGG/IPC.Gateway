using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class ConfiguredChannelTests
{
    [Fact]
    public void Normalize_GroupsLegacyDevicesByProtocolDriver()
    {
        ProjectConfig project = new ProjectConfig();
        project.Devices.Add(CreateDevice("mc-a", PlcProtocol.MitsubishiMc, "legacy.mc"));
        project.Devices.Add(CreateDevice("mc-b", PlcProtocol.MitsubishiMc, "legacy.mc"));
        project.Devices.Add(CreateDevice("s7", PlcProtocol.SiemensS7, "legacy.s7"));

        ProjectConfigStore.Normalize(project);

        Assert.Equal(2, project.Channels.Count);
        Assert.Equal(project.Devices[0].ChannelId, project.Devices[1].ChannelId);
        Assert.NotEqual(project.Devices[0].ChannelId, project.Devices[2].ChannelId);
        Assert.All(project.Devices, device => Assert.False(string.IsNullOrWhiteSpace(device.ChannelId)));
    }

    [Fact]
    public void Scheduler_EnforcesPerChannelConcurrencyWithoutBlockingOtherChannels()
    {
        ProjectConfig project = CreateScheduledProject(out DeviceConfig first, out DeviceConfig second, out DeviceConfig other);
        ConfiguredChannelScheduler scheduler = new ConfiguredChannelScheduler();
        scheduler.Configure(project);

        Assert.True(scheduler.TryAcquirePoll(first, out ConfiguredChannelLease? firstLease));
        Assert.False(scheduler.TryAcquirePoll(second, out _));
        Assert.True(scheduler.TryAcquirePoll(other, out ConfiguredChannelLease? otherLease));

        firstLease?.Dispose();
        otherLease?.Dispose();
    }

    [Fact]
    public async Task Scheduler_GivesWaitingWritePriorityOverNewPolls()
    {
        ProjectConfig project = CreateScheduledProject(out DeviceConfig first, out DeviceConfig second, out _);
        ConfiguredChannelScheduler scheduler = new ConfiguredChannelScheduler();
        scheduler.Configure(project);
        Assert.True(scheduler.TryAcquirePoll(first, out ConfiguredChannelLease? pollLease));

        Task<ConfiguredChannelLease> pendingWrite = scheduler.AcquireWriteAsync(second, CancellationToken.None).AsTask();
        Assert.False(scheduler.TryGetDispatchScore(second, out _));

        pollLease?.Dispose();
        using ConfiguredChannelLease writeLease = await pendingWrite.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static ProjectConfig CreateScheduledProject(
        out DeviceConfig first,
        out DeviceConfig second,
        out DeviceConfig other)
    {
        ChannelConfig mc = new ChannelConfig { Name = "MC", Protocol = PlcProtocol.MitsubishiMc, MaxConcurrentDevicePolls = 1 };
        ChannelConfig s7 = new ChannelConfig { Name = "S7", Protocol = PlcProtocol.SiemensS7, MaxConcurrentDevicePolls = 1 };
        first = CreateDevice("first", PlcProtocol.MitsubishiMc, string.Empty, mc.Id);
        second = CreateDevice("second", PlcProtocol.MitsubishiMc, string.Empty, mc.Id);
        other = CreateDevice("other", PlcProtocol.SiemensS7, string.Empty, s7.Id);
        return new ProjectConfig
        {
            Channels = new List<ChannelConfig> { mc, s7 },
            Devices = new List<DeviceConfig> { first, second, other }
        };
    }

    private static DeviceConfig CreateDevice(string name, PlcProtocol protocol, string driverId, string channelId = "")
    {
        return new DeviceConfig
        {
            Name = name,
            ChannelId = channelId,
            Protocol = protocol,
            Connection = new PlcConnectionOptions { Protocol = protocol, DriverId = driverId }
        };
    }
}
