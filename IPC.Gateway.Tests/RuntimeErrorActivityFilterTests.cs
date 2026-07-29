using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证最近错误只保留仍处于故障状态的记录。
/// </summary>
public sealed class RuntimeErrorActivityFilterTests
{
    /// <summary>
    /// 在线设备不应继续显示历史连接错误和恢复通知。
    /// </summary>
    [Fact]
    public void Filter_RemovesResolvedConnectionErrors()
    {
        IList<RuntimeErrorDetail> result = RuntimeErrorActivityFilter.Filter(
            new List<RuntimeErrorDetail>
            {
                CreateError("DeviceConnection", "连接失败"),
                CreateError("DeviceConnectionRecovered", "连接已恢复"),
                CreateError("Runtime", "需要保留的错误")
            },
            new List<DeviceRuntimeStatus> { CreateDevice(isConnected: true) },
            new List<TagValueSnapshot>());

        RuntimeErrorDetail remaining = Assert.Single(result);
        Assert.Equal("Runtime", remaining.Category);
    }

    /// <summary>
    /// 离线设备的连接错误应继续保留。
    /// </summary>
    [Fact]
    public void Filter_KeepsActiveConnectionError()
    {
        IList<RuntimeErrorDetail> result = RuntimeErrorActivityFilter.Filter(
            new List<RuntimeErrorDetail> { CreateError("DeviceConnection", "连接失败") },
            new List<DeviceRuntimeStatus> { CreateDevice(isConnected: false) },
            new List<TagValueSnapshot>());

        Assert.Single(result);
    }

    /// <summary>
    /// 点位恢复为良好质量后应移除读取错误，仍为读取错误时应保留。
    /// </summary>
    [Fact]
    public void Filter_TracksCurrentTagReadQuality()
    {
        RuntimeErrorDetail error = CreateError("TagRead", "读取失败");
        error.GroupId = "group-1";
        error.TagId = "tag-1";

        IList<RuntimeErrorDetail> recovered = RuntimeErrorActivityFilter.Filter(
            new List<RuntimeErrorDetail> { error },
            new List<DeviceRuntimeStatus> { CreateDevice(isConnected: true) },
            new List<TagValueSnapshot> { CreateTag(TagQuality.Good) });
        IList<RuntimeErrorDetail> failing = RuntimeErrorActivityFilter.Filter(
            new List<RuntimeErrorDetail> { error },
            new List<DeviceRuntimeStatus> { CreateDevice(isConnected: true) },
            new List<TagValueSnapshot> { CreateTag(TagQuality.ReadError) });

        Assert.Empty(recovered);
        Assert.Single(failing);
    }

    /// <summary>
    /// 创建测试设备状态。
    /// </summary>
    private static DeviceRuntimeStatus CreateDevice(bool isConnected)
    {
        return new DeviceRuntimeStatus
        {
            ChannelId = "channel-1",
            DeviceId = "device-1",
            Enabled = true,
            IsConnected = isConnected
        };
    }

    /// <summary>
    /// 创建测试点位快照。
    /// </summary>
    private static TagValueSnapshot CreateTag(TagQuality quality)
    {
        return new TagValueSnapshot
        {
            ChannelId = "channel-1",
            DeviceId = "device-1",
            GroupId = "group-1",
            TagId = "tag-1",
            Quality = quality
        };
    }

    /// <summary>
    /// 创建测试错误记录。
    /// </summary>
    private static RuntimeErrorDetail CreateError(string category, string message)
    {
        return new RuntimeErrorDetail
        {
            Category = category,
            ChannelId = "channel-1",
            DeviceId = "device-1",
            Message = message,
            Timestamp = DateTime.Now
        };
    }
}
