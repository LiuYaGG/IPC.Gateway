using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Persistence;

/// <summary>
/// 使用 UTF-8 JSON 文件独立保存脚本、数据库连接和写入目标配置。
/// </summary>
public sealed class JsonScriptConfigurationStore : IScriptConfigurationStore
{
    private readonly string _filePath;
    private readonly IScriptSecretProtector _secretProtector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// 创建脚本配置文件存储。
    /// </summary>
    public JsonScriptConfigurationStore(GatewayScriptingOptions options, IScriptSecretProtector secretProtector)
    {
        GatewayScriptingOptions normalized = options.Normalize();
        _filePath = Path.GetFullPath(normalized.ConfigurationFile, AppContext.BaseDirectory);
        _secretProtector = secretProtector;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// 异步读取并解密完整脚本配置。
    /// </summary>
    public async Task<ScriptConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return new ScriptConfigurationDocument();

            await using FileStream stream = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ScriptConfigurationDocument document = await JsonSerializer.DeserializeAsync<ScriptConfigurationDocument>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new ScriptConfigurationDocument();
            NormalizeCollections(document);
            foreach (ScriptDatabaseConnectionDefinition connection in document.Connections)
                connection.ConnectionString = _secretProtector.Unprotect(connection.ConnectionString);
            return document.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 异步加密并原子保存完整脚本配置。
    /// </summary>
    public async Task SaveAsync(ScriptConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScriptConfigurationDocument persisted = document.Clone();
            NormalizeCollections(persisted);
            foreach (ScriptDatabaseConnectionDefinition connection in persisted.Connections)
                connection.ConnectionString = _secretProtector.Protect(connection.ConnectionString);

            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = _filePath + ".tmp";
            string json = JsonSerializer.Serialize(persisted, _jsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 补齐反序列化后可能为空的集合。
    /// </summary>
    private static void NormalizeCollections(ScriptConfigurationDocument document)
    {
        document.Connections ??= [];
        document.Targets ??= [];
        document.Scripts ??= [];
        foreach (ScriptDatabaseWriteTarget target in document.Targets)
        {
            target.AllowedColumns ??= [];
            target.KeyColumns ??= [];
        }
    }
}
