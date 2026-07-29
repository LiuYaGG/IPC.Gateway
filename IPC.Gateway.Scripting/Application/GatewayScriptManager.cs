using System.Text.RegularExpressions;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using IPC.Gateway.Scripting.Runtime;

namespace IPC.Gateway.Scripting.Application;

/// <summary>
/// 负责脚本中心配置的校验、增改、删除和安全视图生成。
/// </summary>
public sealed partial class GatewayScriptManager
{
    private readonly IScriptConfigurationStore _store;
    private readonly IScriptRuntimeService _runtime;
    private readonly IScriptDatabaseQueue _databaseQueue;
    private readonly ValueTransformScriptService _valueTransformScripts;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    /// <summary>
    /// 创建脚本中心应用服务。
    /// </summary>
    public GatewayScriptManager(
        IScriptConfigurationStore store,
        IScriptRuntimeService runtime,
        IScriptDatabaseQueue databaseQueue,
        ValueTransformScriptService? valueTransformScripts = null)
    {
        _store = store;
        _runtime = runtime;
        _databaseQueue = databaseQueue;
        _valueTransformScripts = valueTransformScripts ?? new ValueTransformScriptService(store, new GatewayScriptCompiler());
    }

    /// <summary>
    /// 获取隐藏连接字符串后的脚本中心概览。
    /// </summary>
    public async Task<ScriptCenterOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (ScriptDatabaseConnectionDefinition connection in document.Connections)
            connection.ConnectionString = string.IsNullOrWhiteSpace(connection.ConnectionString) ? string.Empty : "********";
        return new ScriptCenterOverview
        {
            Connections = document.Connections,
            Targets = document.Targets,
            Scripts = document.Scripts,
            RuntimeStatuses = _runtime.GetStatuses().ToList(),
            QueueStatus = _databaseQueue.GetStatus()
        };
    }

    /// <summary>
    /// 新增或更新一个数据库连接，并在空密码输入时保留原连接字符串。
    /// </summary>
    public async Task<ScriptDatabaseConnectionDefinition> SaveConnectionAsync(
        ScriptDatabaseConnectionDefinition input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            ScriptDatabaseConnectionDefinition? existing = document.Connections.FirstOrDefault(item => SameId(item.Id, input.Id));
            ScriptDatabaseConnectionDefinition saved = input.Clone();
            saved.Id = NormalizeOrCreateId(saved.Id);
            saved.Name = RequireText(saved.Name, "数据库连接名称");
            saved.ConnectionTimeoutSeconds = Math.Clamp(saved.ConnectionTimeoutSeconds, 1, 120);
            if (string.IsNullOrWhiteSpace(saved.ConnectionString) || saved.ConnectionString == "********")
                saved.ConnectionString = existing?.ConnectionString ?? string.Empty;
            if (string.IsNullOrWhiteSpace(saved.ConnectionString))
                throw new InvalidOperationException("数据库连接字符串不能为空。");
            saved.UpdatedUtc = DateTimeOffset.UtcNow;
            EnsureUniqueName(document.Connections.Where(item => !SameId(item.Id, saved.Id)).Select(item => item.Name), saved.Name, "数据库连接名称");
            ReplaceById(document.Connections, saved, item => item.Id);
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            saved.ConnectionString = "********";
            return saved;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 删除未被写入目标引用的数据库连接。
    /// </summary>
    public async Task DeleteConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (document.Targets.Any(item => SameId(item.ConnectionId, id)))
                throw new InvalidOperationException("该数据库连接仍被写入目标引用，不能删除。");
            RemoveRequired(document.Connections, id, item => item.Id, "数据库连接");
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 新增或更新一个仅允许插入和更新的数据库目标。
    /// </summary>
    public async Task<ScriptDatabaseWriteTarget> SaveTargetAsync(
        ScriptDatabaseWriteTarget input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            ScriptDatabaseWriteTarget saved = input.Clone();
            saved.Id = NormalizeOrCreateId(saved.Id);
            saved.Name = RequireText(saved.Name, "写入目标名称");
            saved.ConnectionId = RequireText(saved.ConnectionId, "数据库连接");
            if (!document.Connections.Any(item => SameId(item.Id, saved.ConnectionId)))
                throw new InvalidOperationException("选择的数据库连接不存在。");
            saved.Table = ValidateIdentifier(saved.Table, "数据表");
            saved.Schema = string.IsNullOrWhiteSpace(saved.Schema) ? string.Empty : ValidateIdentifier(saved.Schema, "架构");
            saved.AllowedColumns = NormalizeIdentifiers(saved.AllowedColumns, "允许字段");
            saved.KeyColumns = NormalizeIdentifiers(saved.KeyColumns, "更新键");
            if (saved.AllowedColumns.Count == 0)
                throw new InvalidOperationException("至少配置一个允许写入字段。");
            if (!saved.AllowInsert && !saved.AllowUpdate)
                throw new InvalidOperationException("写入目标必须至少允许 INSERT 或 UPDATE。");
            if (saved.AllowUpdate && saved.KeyColumns.Count == 0)
                throw new InvalidOperationException("允许 UPDATE 时必须配置更新键。");
            if (saved.KeyColumns.Any(key => !saved.AllowedColumns.Contains(key, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("更新键必须同时存在于允许字段白名单中。");
            saved.MaxAffectedRows = Math.Clamp(saved.MaxAffectedRows, 1, 1000);
            saved.UpdatedUtc = DateTimeOffset.UtcNow;
            EnsureUniqueName(document.Targets.Where(item => !SameId(item.Id, saved.Id)).Select(item => item.Name), saved.Name, "写入目标名称");
            ReplaceById(document.Targets, saved, item => item.Id);
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 删除指定数据库写入目标。
    /// </summary>
    public async Task DeleteTargetAsync(string id, CancellationToken cancellationToken = default)
    {
        await MutateDeleteAsync(id, "数据库写入目标", document => document.Targets, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 新增或更新脚本，并在保存前完成安全与编译校验。
    /// </summary>
    public async Task<GatewayScriptDefinition> SaveScriptAsync(
        GatewayScriptDefinition input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ScriptValidationResult validation = await _runtime.ValidateAsync(input.SourceCode, input.ScriptType, cancellationToken).ConfigureAwait(false);
        if (!validation.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            GatewayScriptDefinition? existing = document.Scripts.FirstOrDefault(item => SameId(item.Id, input.Id));
            GatewayScriptDefinition saved = input.Clone();
            saved.Id = NormalizeOrCreateId(saved.Id);
            saved.Name = RequireText(saved.Name, "脚本名称");
            saved.SourceCode = RequireText(saved.SourceCode, "脚本内容");
            saved.IntervalSeconds = Math.Clamp(saved.IntervalSeconds, 1, 86400);
            saved.DebounceMilliseconds = Math.Clamp(saved.DebounceMilliseconds, 0, 60000);
            saved.TimeoutSeconds = Math.Clamp(saved.TimeoutSeconds, 1, 300);
            saved.MaxWritesPerExecution = Math.Clamp(saved.MaxWritesPerExecution, 1, 100);
            saved.TransformTimeoutMilliseconds = Math.Clamp(saved.TransformTimeoutMilliseconds, 10, 5000);
            if (saved.ScriptType == GatewayScriptType.ValueTransform)
            {
                saved.TriggerType = ScriptTriggerType.Manual;
                saved.TriggerTagPath = string.Empty;
                saved.AllowedWriteTagPaths = [];
                saved.NodeCategory = saved.ValueTransformScope == ValueTransformScriptScope.TagCleaning
                    ? string.Empty
                    : RequireText(saved.NodeCategory, "节点库分类");
                saved.InputDataType = ValidateValueDataType(saved.InputDataType, "输入数据类型");
                saved.OutputDataType = ValidateValueDataType(saved.OutputDataType, "输出数据类型");
                saved.PublishedVersion = existing?.PublishedVersion ?? 0;
                saved.PublishedSourceCode = existing?.PublishedSourceCode ?? string.Empty;
                saved.PublishedUtc = existing?.PublishedUtc;
                saved.PublishedVersions = existing?.PublishedVersions?.Select(item => item.Clone()).ToList() ?? [];
            }
            else if (saved.TriggerType == ScriptTriggerType.TagChanged)
                saved.TriggerTagPath = ValidateTagPath(saved.TriggerTagPath, "触发点位路径");
            else
                saved.TriggerTagPath = string.Empty;
            if (saved.ScriptType == GatewayScriptType.TagLinkage)
            {
                saved.AllowedWriteTagPaths = (saved.AllowedWriteTagPaths ?? [])
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => ValidateTagPath(path, "允许写入点位路径"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (saved.AllowedWriteTagPaths.Count == 0)
                    throw new InvalidOperationException("点位联动脚本至少需要选择一个允许写入的目标点位。");
            }
            else
            {
                saved.AllowedWriteTagPaths = [];
            }
            saved.CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow;
            saved.UpdatedUtc = DateTimeOffset.UtcNow;
            saved.Version = Math.Max(1, (existing?.Version ?? 0) + 1);
            EnsureUniqueName(document.Scripts.Where(item => !SameId(item.Id, saved.Id)).Select(item => item.Name), saved.Name, "脚本名称");
            ReplaceById(document.Scripts, saved, item => item.Id);
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            await _runtime.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 删除指定脚本定义。
    /// </summary>
    public async Task DeleteScriptAsync(string id, CancellationToken cancellationToken = default)
    {
        await MutateDeleteAsync(id, "脚本", document => document.Scripts, cancellationToken).ConfigureAwait(false);
        await _runtime.ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 将值处理脚本当前草稿发布为可供规则和标签清洗引用的固定版本。
    /// </summary>
    public async Task<GatewayScriptDefinition> PublishValueScriptAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            GatewayScriptDefinition script = document.Scripts.FirstOrDefault(item => SameId(item.Id, id)) ??
                                             throw new KeyNotFoundException("未找到指定脚本。");
            if (script.ScriptType != GatewayScriptType.ValueTransform)
                throw new InvalidOperationException("只有值处理脚本支持发布版本。");

            ScriptValidationResult validation = await _runtime
                .ValidateAsync(script.SourceCode, GatewayScriptType.ValueTransform, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

            script.PublishedVersion = Math.Max(1, script.Version);
            script.PublishedSourceCode = script.SourceCode;
            script.PublishedUtc = DateTimeOffset.UtcNow;
            script.PublishedVersions ??= [];
            script.PublishedVersions.RemoveAll(item => item.Version == script.PublishedVersion);
            script.PublishedVersions.Add(new ValueTransformPublishedVersion
            {
                Version = script.PublishedVersion,
                SourceCode = script.PublishedSourceCode,
                PublishedUtc = script.PublishedUtc.Value
            });
            script.UpdatedUtc = DateTimeOffset.UtcNow;
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            await _runtime.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return script.Clone();
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 使用测试值执行尚未发布的值处理脚本草稿。
    /// </summary>
    public ValueTransformExecutionResult TestValueScript(ValueTransformScriptTestRequest request)
    {
        return _valueTransformScripts.Test(request);
    }

    /// <summary>
    /// 测试指定数据库连接，不执行任何查询和写入语句。
    /// </summary>
    public async Task TestConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await _databaseQueue.TestConnectionAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行通用配置删除操作并保存文档。
    /// </summary>
    private async Task MutateDeleteAsync<T>(
        string id,
        string displayName,
        Func<ScriptConfigurationDocument, List<T>> selector,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            RemoveRequired(selector(document), id, item => item switch
            {
                ScriptDatabaseWriteTarget target => target.Id,
                GatewayScriptDefinition script => script.Id,
                _ => string.Empty
            }, displayName);
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 规范化或创建配置标识。
    /// </summary>
    private static string NormalizeOrCreateId(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
    }

    /// <summary>
    /// 校验并返回必填文本。
    /// </summary>
    private static string RequireText(string? value, string displayName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > 0 ? normalized : throw new InvalidOperationException($"{displayName}不能为空。");
    }

    /// <summary>
    /// 校验数据库标识符并拒绝可形成 SQL 语句的字符。
    /// </summary>
    private static string ValidateIdentifier(string? value, string displayName)
    {
        string normalized = RequireText(value, displayName);
        if (!SafeIdentifierRegex().IsMatch(normalized))
            throw new InvalidOperationException($"{displayName}“{normalized}”不是安全的数据库标识符。");
        return normalized;
    }

    /// <summary>
    /// 规范化数据库字段列表并去除重复值。
    /// </summary>
    private static List<string> NormalizeIdentifiers(IEnumerable<string>? values, string displayName)
    {
        return (values ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => ValidateIdentifier(item, displayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 校验点位路径必须由四段标识组成。
    /// </summary>
    private static string ValidateTagPath(string? path, string displayName)
    {
        string normalized = RequireText(path, displayName);
        string[] parts = normalized.Split('/');
        if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[3]))
            throw new InvalidOperationException($"{displayName}必须为 ChannelId/DeviceId/GroupId/TagId，设备直属点位的 GroupId 留空但保留斜杠。");
        return string.Join("/", parts.Select(part => part.Trim()));
    }

    /// <summary>
    /// 校验值处理脚本声明的输入和输出数据类型。
    /// </summary>
    private static string ValidateValueDataType(string? value, string displayName)
    {
        string normalized = RequireText(value, displayName);
        string[] supported =
        [
            "Bool", "Int8", "UInt8", "Int16", "UInt16", "Int32", "UInt32",
            "Int64", "UInt64", "Float", "Double", "Decimal", "String", "DateTime", "Object",
            "BoolArray", "Int8Array", "UInt8Array", "Int16Array", "UInt16Array",
            "Int32Array", "UInt32Array", "Int64Array", "UInt64Array", "FloatArray", "DoubleArray"
        ];
        string? match = supported.FirstOrDefault(item =>
            string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException($"{displayName}“{normalized}”不受支持。");
    }

    /// <summary>
    /// 确保同类配置名称不重复。
    /// </summary>
    private static void EnsureUniqueName(IEnumerable<string> names, string name, string displayName)
    {
        if (names.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{displayName}“{name}”已经存在。");
    }

    /// <summary>
    /// 按标识替换现有项目或追加新项目。
    /// </summary>
    private static void ReplaceById<T>(List<T> items, T saved, Func<T, string> idSelector)
    {
        int index = items.FindIndex(item => SameId(idSelector(item), idSelector(saved)));
        if (index >= 0)
            items[index] = saved;
        else
            items.Add(saved);
    }

    /// <summary>
    /// 删除必须存在的配置项目。
    /// </summary>
    private static void RemoveRequired<T>(List<T> items, string id, Func<T, string> idSelector, string displayName)
    {
        int removed = items.RemoveAll(item => SameId(idSelector(item), id));
        if (removed == 0)
            throw new KeyNotFoundException($"未找到指定的{displayName}。");
    }

    /// <summary>
    /// 忽略大小写比较两个配置标识。
    /// </summary>
    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 创建允许 Unicode 字母、数字、下划线和美元符号的数据库标识符规则。
    /// </summary>
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{Nd}_$]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierRegex();
}
