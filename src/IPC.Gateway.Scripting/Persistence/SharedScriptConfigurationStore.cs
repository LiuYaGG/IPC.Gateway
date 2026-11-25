using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Persistence;

/// <summary>Shares script definitions and published versions, preserving the original local file for recovery.</summary>
public sealed class SharedScriptConfigurationStore(
    GatewaySharedArtifactRepository repository,
    JsonScriptConfigurationStore legacy,
    IScriptSecretProtector protector,
    IGatewayDatabaseOwner? owner = null) : IScriptConfigurationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScriptConfigurationDocument? _cached;
    public long CurrentRevision => Volatile.Read(ref _cached)?.PersistenceRevision ?? 0;

    /// <summary>Reads the current committed revision before promotion or publication checks.</summary>
    public Task<ScriptConfigurationDocument> LoadLatestAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(refresh: true, cancellationToken);

    public Task<ScriptConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(refresh: false, cancellationToken);

    private async Task<ScriptConfigurationDocument> LoadCoreAsync(bool refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The collection published here is immutable. Slow revision probes must not
        // hold up per-tag database enqueues that already have a valid configuration.
        var cached = Volatile.Read(ref _cached);
        if (!refresh && cached != null) return cached.Clone();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_cached != null && !refresh) return _cached.Clone();
            long revision = repository.ProbeRevision("scripts");
            if (_cached?.PersistenceRevision == revision)
            {
                return _cached.Clone();
            }
            SharedArtifactSnapshot? shared = repository.Read("scripts");
            if (shared == null)
            {
                if (owner?.IsCluster == true && !owner.CanRunBusiness)
                    throw new InvalidOperationException("共享脚本尚未初始化，请先在原主节点完成本地脚本迁移。");
                var original = await legacy.LoadAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    original.PersistenceRevision = repository.Save("scripts", 0, Encode(original));
                    Volatile.Write(ref _cached, original.Clone());
                }
                catch (InvalidOperationException)
                {
                    // Another authorized node may have imported first. Shared data wins.
                    shared = repository.Read("scripts");
                    if (shared == null) throw;
                }
            }
            if (shared != null) Volatile.Write(ref _cached, Decode(shared));
            return _cached!.Clone();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ScriptConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long revision = repository.Save("scripts", document.PersistenceRevision, Encode(document));
                document.PersistenceRevision = revision;
                Volatile.Write(ref _cached, document.Clone());
            }
            catch
            {
                Volatile.Write(ref _cached, null);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private byte[] Encode(ScriptConfigurationDocument document)
    {
        var persisted = document.Clone();
        JsonScriptConfigurationStore.NormalizeCollections(persisted);
        foreach (var connection in persisted.Connections)
            connection.ConnectionString = protector.Protect(connection.ConnectionString);
        return JsonSerializer.SerializeToUtf8Bytes(persisted, Json);
    }

    private ScriptConfigurationDocument Decode(SharedArtifactSnapshot snapshot)
    {
        var document = JsonSerializer.Deserialize<ScriptConfigurationDocument>(snapshot.Payload, Json)
            ?? throw new InvalidDataException("共享脚本配置为空或损坏。");
        JsonScriptConfigurationStore.NormalizeCollections(document);
        foreach (var connection in document.Connections)
        {
            connection.ConnectionString = protector.Unprotect(connection.ConnectionString);
            if (connection.ConnectionString.StartsWith(GatewaySecretProtector.Prefix, StringComparison.Ordinal))
                throw new InvalidDataException("共享脚本数据库连接解密失败，请在两台网关配置相同的业务加密密钥。");
        }
        document.PersistenceRevision = snapshot.Revision;
        return document;
    }
}
