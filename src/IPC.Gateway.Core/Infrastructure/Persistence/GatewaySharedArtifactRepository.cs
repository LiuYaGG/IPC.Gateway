using System.Globalization;
using System.Security.Cryptography;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed record SharedArtifactSnapshot(long Revision, byte[] Payload);

/// <summary>Shared, versioned script/model definitions and immutable content-addressed model files.</summary>
public sealed class GatewaySharedArtifactRepository
{
    private const int ChunkSize = 1024 * 1024;
    private const int MaxBlobBytes = 1024 * 1024 * 1024;
    private readonly SqlSugarConnectionFactory _factory;
    private readonly IGatewayDatabaseOwner? _owner;

    public GatewaySharedArtifactRepository(SqlSugarConnectionFactory factory, IGatewayDatabaseOwner? owner = null)
    {
        _factory = factory;
        _owner = owner;
        new GatewayDatabaseMigrator(factory).Migrate();
    }

    private ISqlSugarClient Open()
    {
        var db = _factory.Create();
        db.Ado.CommandTimeOut = 10;
        return db;
    }

    private static void ValidateKind(string kind)
    {
        if (kind is not ("scripts" or "models")) throw new ArgumentException("Unknown shared artifact kind.", nameof(kind));
    }

    public SharedArtifactSnapshot? Read(string kind)
    {
        ValidateKind(kind);
        using var db = Open();
        var table = db.Ado.GetDataTable("select revision,payload from gateway_shared_artifacts where kind=@kind",
            new SugarParameter("@kind", kind));
        if (table.Rows.Count != 1) throw new InvalidDataException("Shared artifact schema is incomplete.");
        var row = table.Rows[0];
        long revision = Convert.ToInt64(row["revision"], CultureInfo.InvariantCulture);
        return revision == 0 ? null : new(revision, (byte[])row["payload"]);
    }

    public long ProbeRevision(string kind)
    {
        ValidateKind(kind);
        using var db = Open();
        return Convert.ToInt64(db.Ado.GetScalar("select revision from gateway_shared_artifacts where kind=@kind",
            new SugarParameter("@kind", kind)), CultureInfo.InvariantCulture);
    }

    public long Save(string kind, long expectedRevision, byte[] payload, IReadOnlyDictionary<string, byte[]>? blobs = null)
    {
        var streams = blobs?.ToDictionary(x => x.Key, x => (Stream)new MemoryStream(x.Value, writable: false));
        try { return SaveCore(kind, expectedRevision, payload, streams); }
        finally { if (streams != null) foreach (var stream in streams.Values) stream.Dispose(); }
    }

    public long SaveStream(string kind, long expectedRevision, byte[] payload, IReadOnlyDictionary<string, string> blobFiles)
    {
        var streams = new Dictionary<string, Stream>();
        try
        {
            foreach (var (hash, path) in blobFiles) streams.Add(hash, File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            return SaveCore(kind, expectedRevision, payload, streams);
        }
        finally { foreach (var stream in streams.Values) stream.Dispose(); }
    }

    private long SaveCore(string kind, long expectedRevision, byte[] payload, IReadOnlyDictionary<string, Stream>? blobs)
    {
        ValidateKind(kind);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || payload.Length > 16 * 1024 * 1024)
            throw new ArgumentException("Invalid artifact revision or document size.");
        if (kind != "models" && blobs?.Count > 0) throw new ArgumentException("Only models have binary artifacts.");
        foreach (var (hash, stream) in blobs ?? new Dictionary<string, Stream>())
        {
            if (stream.Length > MaxBlobBytes || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model file digest or size is invalid.");
            stream.Position = 0;
        }
        // Upload immutable contents without holding the business ownership row.
        // Only the short metadata transaction publishes the new model revision.
        foreach (var (hash, stream) in blobs ?? new Dictionary<string, Stream>())
            StageBlob(hash, stream);
        using var db = Open();
        db.Ado.BeginTran();
        try
        {
            GatewayDatabaseOwnership.GuardTransaction(db, _owner);
            int changed = db.Ado.ExecuteCommand(
                "update gateway_shared_artifacts set revision=@next,payload=@payload where kind=@kind and revision=@expected",
                new SugarParameter("@next", expectedRevision + 1), new SugarParameter("@payload", payload),
                new SugarParameter("@kind", kind), new SugarParameter("@expected", expectedRevision));
            if (changed != 1) throw new InvalidOperationException("共享配置已被其他操作修改，请重新加载后再保存。");
            if (_owner?.IsCluster == true && !_owner.CanRunBusiness) throw new InvalidOperationException("HA_NOT_ACTIVE");
            db.Ado.CommitTran();
            return expectedRevision + 1;
        }
        catch { db.Ado.RollbackTran(); throw; }
    }

    private void StageBlob(string hash, Stream stream)
    {
        if (_owner?.IsCluster == true && !_owner.CanRunBusiness) throw new InvalidOperationException("HA_NOT_ACTIVE");
        using var db = Open();
        var key = new SugarParameter("@hash", hash.ToUpperInvariant());
        if (db.Ado.GetInt("select count(*) from gateway_shared_blobs where digest=@hash", key) != 0) return;
        db.Ado.BeginTran();
        try
        {
            db.Ado.ExecuteCommand("insert into gateway_shared_blobs(digest,byte_count) values(@hash,@size)",
                key, new SugarParameter("@size", stream.Length));
            byte[] buffer = new byte[ChunkSize];
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int part = 0;
            for (int length; (length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)) > 0;)
            {
                if (_owner?.IsCluster == true && !_owner.CanRunBusiness) throw new InvalidOperationException("HA_NOT_ACTIVE");
                digest.AppendData(buffer, 0, length);
                db.Ado.ExecuteCommand("insert into gateway_shared_blob_chunks(digest,part,data) values(@hash,@part,@data)",
                    key, new SugarParameter("@part", part++), new SugarParameter("@data", buffer.AsSpan(0, length).ToArray()));
            }
            if (!string.Equals(Convert.ToHexString(digest.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The model file changed during upload.");
            if (_owner?.IsCluster == true && !_owner.CanRunBusiness) throw new InvalidOperationException("HA_NOT_ACTIVE");
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            // A concurrent uploader may have committed this same immutable digest.
            if (_owner?.IsCluster == true && !_owner.CanRunBusiness ||
                Convert.ToInt64(db.Ado.GetScalar("select byte_count from gateway_shared_blobs where digest=@hash", key) ?? -1, CultureInfo.InvariantCulture) != stream.Length)
                throw;
        }
    }

    public byte[]? ReadBlob(string sha256)
    {
        using var result = new MemoryStream();
        return CopyBlob(sha256, result) ? result.ToArray() : null;
    }

    public void CopyBlobTo(string sha256, string destination)
    {
        bool created = false;
        try
        {
            using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            if (!CopyBlob(sha256, file)) throw new FileNotFoundException("The shared model content is missing.");
            file.Flush(true);
        }
        catch { if (created) File.Delete(destination); throw; }
    }

    private bool CopyBlob(string sha256, Stream result)
    {
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid model digest.");
        using var db = Open();
        var key = new SugarParameter("@hash", sha256.ToUpperInvariant());
        object? value = db.Ado.GetScalar("select byte_count from gateway_shared_blobs where digest=@hash", key);
        if (value is null or DBNull) return false;
        int size = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (size is < 0 or > MaxBlobBytes) throw new InvalidDataException("Stored model size exceeds the supported limit.");
        int part = 0;
        long written = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (written < size)
        {
            var chunk = db.Ado.GetScalar("select data from gateway_shared_blob_chunks where digest=@hash and part=@part",
                key, new SugarParameter("@part", part++));
            if (chunk is not byte[] bytes || bytes.Length == 0 || bytes.Length > ChunkSize || written + bytes.Length > size)
                throw new InvalidDataException("Stored model chunks are invalid.");
            result.Write(bytes);
            digest.AppendData(bytes);
            written += bytes.Length;
        }
        if (!string.Equals(Convert.ToHexString(digest.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stored model file failed digest verification.");
        return true;
    }
}
