using IPC.Gateway.WebHost;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证脚本点位写入始终使用项目配置中的权威数据类型。
/// </summary>
public sealed class GatewayScriptTagMetadataResolverTests
{
    /// <summary>
    /// 设备直属标签的运行快照类型为空时仍应返回配置类型。
    /// </summary>
    [Fact]
    public void ResolveDataType_DirectTagWithEmptySnapshotType_ReturnsConfiguredType()
    {
        ProjectConfig project = CreateProject(grouped: false, PlcDataType.Double);
        TagValueSnapshot snapshot = CreateSnapshot(grouped: false, dataType: string.Empty);

        string result = GatewayScriptTagMetadataResolver.ResolveDataType(project, snapshot);

        Assert.Equal(nameof(PlcDataType.Double), result);
    }

    /// <summary>
    /// 分组标签的运行快照类型错误时仍应返回配置类型。
    /// </summary>
    [Fact]
    public void ResolveDataType_GroupedTagWithStaleSnapshotType_ReturnsConfiguredType()
    {
        ProjectConfig project = CreateProject(grouped: true, PlcDataType.Bool);
        TagValueSnapshot snapshot = CreateSnapshot(grouped: true, dataType: nameof(PlcDataType.Int32));

        string result = GatewayScriptTagMetadataResolver.ResolveDataType(project, snapshot);

        Assert.Equal(nameof(PlcDataType.Bool), result);
    }

    /// <summary>
    /// 创建包含一个测试标签的项目配置。
    /// </summary>
    private static ProjectConfig CreateProject(bool grouped, PlcDataType dataType)
    {
        TagConfig tag = new()
        {
            Id = "tag-1",
            DeviceId = "device-1",
            GroupId = grouped ? "group-1" : string.Empty,
            DataType = dataType
        };
        DeviceConfig device = new()
        {
            Id = "device-1",
            ChannelId = "channel-1"
        };
        if (grouped)
        {
            device.Groups.Add(new GroupConfig
            {
                Id = "group-1",
                DeviceId = device.Id,
                Tags = [tag]
            });
        }
        else
        {
            device.Tags.Add(tag);
        }

        return new ProjectConfig
        {
            Devices = [device]
        };
    }

    /// <summary>
    /// 创建用于定位测试标签的运行快照。
    /// </summary>
    private static TagValueSnapshot CreateSnapshot(bool grouped, string dataType)
    {
        return new TagValueSnapshot
        {
            ChannelId = "channel-1",
            DeviceId = "device-1",
            GroupId = grouped ? "group-1" : string.Empty,
            TagId = "tag-1",
            DataType = dataType
        };
    }
}
