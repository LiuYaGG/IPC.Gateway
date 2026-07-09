using IPC.Gateway.Core.Domain.Gateway;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Tests;

public sealed class GatewayProjectAggregateTagTests
{
    [Fact]
    public void UpdateTag_PreservesCleaningConfiguration()
    {
        TagConfig tag = new TagConfig
        {
            Id = "tag-1",
            Name = "Temperature",
            Address = "D100",
            DataType = PlcDataType.Int16,
            Cleaning = DataCleaningConfig.Default()
        };
        ProjectConfig project = new ProjectConfig
        {
            Devices = new List<DeviceConfig>
            {
                new DeviceConfig
                {
                    Id = "device-1",
                    Name = "D1002",
                    Protocol = PlcProtocol.VirtualPlc,
                    Tags = new List<TagConfig> { tag }
                }
            }
        };
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);

        TagConfig input = new TagConfig
        {
            Id = "ignored",
            Name = "Temperature",
            Address = "D100",
            DataType = PlcDataType.Int16,
            Enabled = true,
            AccessMode = TagAccessMode.ReadWrite,
            Cleaning = new DataCleaningConfig
            {
                Enabled = true,
                OutOfRangeEnabled = true,
                MinValue = 10D,
                MaxValue = 20D,
                DeadbandEnabled = true,
                Deadband = 0.5D,
                PreserveLastGoodOnFilter = true
            }
        };

        TagConfig updated = aggregate.UpdateTag("tag-1", input);

        Assert.True(updated.Cleaning.Enabled);
        Assert.True(updated.Cleaning.OutOfRangeEnabled);
        Assert.Equal(10D, updated.Cleaning.MinValue);
        Assert.Equal(20D, updated.Cleaning.MaxValue);
        Assert.True(updated.Cleaning.DeadbandEnabled);
        Assert.Equal(0.5D, updated.Cleaning.Deadband);
    }
}
