using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using IPC.Gateway.Scripting.Runtime;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证点位联动脚本写入 API 的白名单、数量限制和来源传递。
/// </summary>
public sealed class ScriptTagWriteApiTests
{
    /// <summary>
    /// 验证白名单内的点位可以写入并携带联动来源。
    /// </summary>
    [Fact]
    public async Task SetAsync_AllowedPath_ShouldWriteWithContext()
    {
        FakeTagAccessor accessor = new();
        ScriptTagWriteApi api = CreateApi(accessor, ["Channel/Device/Group/Target"], 2);

        await api.SetAsync("channel/device/group/target", 42);

        Assert.Equal("channel/device/group/target", accessor.LastWritePath);
        Assert.Equal(42, accessor.LastWriteValue);
        Assert.Equal("script-1", accessor.LastWriteContext?.ScriptId);
        Assert.Equal(1, accessor.LastWriteContext?.LinkageDepth);
    }

    /// <summary>
    /// 验证名称路径解析到白名单中的 ID 路径后可以正常写入。
    /// </summary>
    [Fact]
    public async Task SetAsync_NamePathResolvingToAllowedIdPath_ShouldWrite()
    {
        const string canonicalPath = "channel-id/device-id/group-id/tag-id";
        FakeTagAccessor accessor = new()
        {
            ResolvedValue = new ScriptTagValue { Path = canonicalPath }
        };
        ScriptTagWriteApi api = CreateApi(accessor, [canonicalPath], 2);

        await api.SetAsync("通道名称/设备名称/分组名称/标签名称", 42);

        Assert.Equal(canonicalPath, accessor.LastWritePath);
    }

    /// <summary>
    /// 验证白名单外的点位写入会被拒绝。
    /// </summary>
    [Fact]
    public async Task SetAsync_PathOutsideWhitelist_ShouldBeRejected()
    {
        FakeTagAccessor accessor = new();
        ScriptTagWriteApi api = CreateApi(accessor, ["channel/device/group/target"], 2);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.SetAsync("channel/device/group/other", 42));

        Assert.Contains("白名单", exception.Message, StringComparison.Ordinal);
        Assert.Null(accessor.LastWritePath);
    }

    /// <summary>
    /// 验证单次执行超过配置写入数量时会被拒绝。
    /// </summary>
    [Fact]
    public async Task SetAsync_ExceedsExecutionLimit_ShouldBeRejected()
    {
        FakeTagAccessor accessor = new();
        ScriptTagWriteApi api = CreateApi(
            accessor,
            ["channel/device/group/target1", "channel/device/group/target2"],
            1);

        await api.SetAsync("channel/device/group/target1", 1);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.SetAsync("channel/device/group/target2", 2));

        Assert.Contains("单次最多", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 创建用于测试的受控点位写入 API。
    /// </summary>
    private static ScriptTagWriteApi CreateApi(
        IScriptTagAccessor accessor,
        IEnumerable<string> allowedPaths,
        int maxWrites)
    {
        return new ScriptTagWriteApi(
            accessor,
            allowedPaths,
            maxWrites,
            new ScriptTagWriteContext
            {
                ScriptId = "script-1",
                CorrelationId = "correlation-1",
                LinkageDepth = 1
            },
            new ScriptLogCollector(),
            CancellationToken.None,
            enabled: true);
    }

    /// <summary>
    /// 记录测试期间收到的点位写入请求。
    /// </summary>
    private sealed class FakeTagAccessor : IScriptTagAccessor
    {
        public event EventHandler<ScriptTagChangedEventArgs>? TagChanged
        {
            add { }
            remove { }
        }
        public string? LastWritePath { get; private set; }
        public object? LastWriteValue { get; private set; }
        public ScriptTagWriteContext? LastWriteContext { get; private set; }
        public ScriptTagValue? ResolvedValue { get; init; }

        /// <summary>
        /// 测试替身不提供点位读取。
        /// </summary>
        public ScriptTagValue? Read(string path) => ResolvedValue;

        /// <summary>
        /// 测试替身不提供批量点位读取。
        /// </summary>
        public IReadOnlyList<ScriptTagValue> ReadMany(IEnumerable<string> paths) => [];

        /// <summary>
        /// 记录收到的点位写入参数。
        /// </summary>
        public Task WriteAsync(
            string path,
            object? value,
            ScriptTagWriteContext writeContext,
            CancellationToken cancellationToken = default)
        {
            LastWritePath = path;
            LastWriteValue = value;
            LastWriteContext = writeContext.Clone();
            return Task.CompletedTask;
        }
    }
}
