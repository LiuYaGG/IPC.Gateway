using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Application;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证脚本中心保存点位联动脚本时的配置约束。
/// </summary>
public sealed class GatewayScriptManagerTests
{
    /// <summary>
    /// 验证点位联动脚本未选择写入白名单时不能保存。
    /// </summary>
    [Fact]
    public async Task SaveScriptAsync_TagLinkageWithoutTargets_ShouldBeRejected()
    {
        GatewayScriptManager manager = CreateManager(out _);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SaveScriptAsync(CreateLinkageScript([])));

        Assert.Contains("至少需要选择一个", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证点位联动脚本会去重写入白名单并限制单次写入数量。
    /// </summary>
    [Fact]
    public async Task SaveScriptAsync_TagLinkageWithTargets_ShouldNormalizeConfiguration()
    {
        GatewayScriptManager manager = CreateManager(out InMemoryConfigurationStore store);
        GatewayScriptDefinition input = CreateLinkageScript([
            "channel/device/group/target",
            "CHANNEL/DEVICE/GROUP/TARGET"
        ]);
        input.MaxWritesPerExecution = 500;

        GatewayScriptDefinition saved = await manager.SaveScriptAsync(input);

        Assert.Equal(GatewayScriptType.TagLinkage, saved.ScriptType);
        Assert.Single(saved.AllowedWriteTagPaths);
        Assert.Equal(100, saved.MaxWritesPerExecution);
        Assert.Single((await store.LoadAsync()).Scripts);
    }

    /// <summary>
    /// 验证值处理脚本再次发布后仍保留旧版本，供已有规则和标签继续固定引用。
    /// </summary>
    [Fact]
    public async Task PublishValueScriptAsync_MultipleDrafts_ShouldKeepPublishedHistory()
    {
        GatewayScriptManager manager = CreateManager(out InMemoryConfigurationStore store);
        GatewayScriptDefinition draft = new()
        {
            Id = "value-script-1",
            Name = "正弦处理",
            ScriptType = GatewayScriptType.ValueTransform,
            SourceCode = "return Math.Sin(Input.AsDouble());",
            InputDataType = "Double",
            OutputDataType = "Double"
        };

        GatewayScriptDefinition firstDraft = await manager.SaveScriptAsync(draft);
        GatewayScriptDefinition firstPublished = await manager.PublishValueScriptAsync(firstDraft.Id);
        firstDraft.SourceCode = "return Math.Cos(Input.AsDouble());";
        GatewayScriptDefinition secondDraft = await manager.SaveScriptAsync(firstDraft);
        GatewayScriptDefinition secondPublished = await manager.PublishValueScriptAsync(secondDraft.Id);

        GatewayScriptDefinition saved = Assert.Single((await store.LoadAsync()).Scripts);
        Assert.Equal(1, firstPublished.PublishedVersion);
        Assert.Equal(2, secondPublished.PublishedVersion);
        Assert.Equal(2, saved.PublishedVersions.Count);
        Assert.Contains(saved.PublishedVersions, item => item.Version == 1);
        Assert.Contains(saved.PublishedVersions, item => item.Version == 2);
    }

    /// <summary>
    /// 创建带内存替身的脚本应用服务。
    /// </summary>
    private static GatewayScriptManager CreateManager(out InMemoryConfigurationStore store)
    {
        store = new InMemoryConfigurationStore();
        return new GatewayScriptManager(store, new FakeRuntimeService(), new FakeDatabaseQueue());
    }

    /// <summary>
    /// 创建用于测试的点位联动脚本定义。
    /// </summary>
    private static GatewayScriptDefinition CreateLinkageScript(List<string> targets)
    {
        return new GatewayScriptDefinition
        {
            Id = "script-1",
            Name = "点位联动测试",
            ScriptType = GatewayScriptType.TagLinkage,
            TriggerType = ScriptTriggerType.Manual,
            SourceCode = "return 1;",
            AllowedWriteTagPaths = targets
        };
    }

    /// <summary>
    /// 使用内存保存脚本配置文档。
    /// </summary>
    private sealed class InMemoryConfigurationStore : IScriptConfigurationStore
    {
        private ScriptConfigurationDocument _document = new();

        /// <summary>
        /// 返回当前内存配置副本。
        /// </summary>
        public Task<ScriptConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_document.Clone());
        }

        /// <summary>
        /// 保存输入配置的副本。
        /// </summary>
        public Task SaveAsync(ScriptConfigurationDocument document, CancellationToken cancellationToken = default)
        {
            _document = document.Clone();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 提供始终通过校验的脚本运行时替身。
    /// </summary>
    private sealed class FakeRuntimeService : IScriptRuntimeService
    {
        /// <summary>
        /// 测试替身无需执行重新加载。
        /// </summary>
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>
        /// 返回成功的默认编译检查结果。
        /// </summary>
        public Task<ScriptValidationResult> ValidateAsync(string sourceCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptValidationResult { Success = true });
        }

        /// <summary>
        /// 返回成功的类型化编译检查结果。
        /// </summary>
        public Task<ScriptValidationResult> ValidateAsync(
            string sourceCode,
            GatewayScriptType scriptType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptValidationResult { Success = true });
        }

        /// <summary>
        /// 测试替身不执行手动脚本。
        /// </summary>
        public Task<ScriptExecutionResult> ExecuteManualAsync(string scriptId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptExecutionResult { ScriptId = scriptId });
        }

        /// <summary>
        /// 测试替身没有运行状态。
        /// </summary>
        public IReadOnlyList<ScriptRuntimeStatus> GetStatuses() => [];
    }

    /// <summary>
    /// 提供不实际访问数据库的队列替身。
    /// </summary>
    private sealed class FakeDatabaseQueue : IScriptDatabaseQueue
    {
        /// <summary>
        /// 返回模拟入队回执。
        /// </summary>
        public Task<ScriptDatabaseWriteReceipt> EnqueueAsync(
            ScriptDatabaseWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptDatabaseWriteReceipt { RequestId = request.Id, Queued = true });
        }

        /// <summary>
        /// 返回空队列状态。
        /// </summary>
        public ScriptDatabaseQueueStatus GetStatus() => new();

        /// <summary>
        /// 测试替身不建立数据库连接。
        /// </summary>
        public Task TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
