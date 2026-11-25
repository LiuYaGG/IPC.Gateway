using System.IO.Compression;
using System.Buffers;
using System.Security.Cryptography;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed record HaReplicaStatus(string State, DateTimeOffset? CheckedAt, long? PendingBytes, string? Volume);

// The peer mounts are read-only. Each gateway writes solely to its own history and replica directories.
internal sealed class HaHistoryReplica(GatewayHaClient ha, IConfiguration configuration, IServiceProvider services,
    ILogger<HaHistoryReplica> logger) : BackgroundService
{
    private readonly string[] _sources = configuration.GetSection("Gateway:HighAvailability:PeerHistoryDirectories").Get<string[]>() ?? [];
    private readonly int _copyBudget = Math.Clamp(configuration.GetValue("Gateway:HighAvailability:HistoryReplicaMiBPerPass",8),1,64)*1024*1024;
    private readonly Dictionary<string,(long Length, long Ticks)> _verified = new(StringComparer.Ordinal);
    private HaReplicaStatus _status = new("NotConfigured", null, null, null);
    public HaReplicaStatus Status => Volatile.Read(ref _status);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_sources.Length == 0 || ha.Endpoints.Count == 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!ha.IsCluster || !services.GetRequiredService<GatewayStartupState>().IsReady) continue;
            try
            {
                var options = services.GetRequiredService<GatewayCoreService>().CurrentHistoryOptions;
                var hot = Path.GetFullPath(options.Directory.Replace('\\',Path.DirectorySeparatorChar),AppContext.BaseDirectory);
                var dest = Path.Combine(hot,".ha-replica");
                Directory.CreateDirectory(dest);
                var expectedNode = ha.NodeId == "A" ? "B" : "A";
                var sources = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
                foreach (var path in _sources)
                {
                    if (!Path.IsPathFullyQualified(path) || string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                        Path.TrimEndingDirectorySeparator(hot),StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Use separate absolute peer history paths.");
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles($"values-????????-ha-{expectedNode}-*.jsonl*"))
                    {
                        var name = file.Name.EndsWith(".gz",StringComparison.Ordinal) ? file.Name[..^3] : file.Name;
                        if (!ValidName(name,expectedNode)) continue;
                        if (!sources.ContainsKey(name) || file.Extension != ".gz") sources[name] = file;
                    }
                }
                long pending = 0;
                var now = DateTimeOffset.UtcNow;
                foreach (var (name, source) in sources.OrderByDescending(x=>x.Value.LastWriteTimeUtc))
                {
                    ct.ThrowIfCancellationRequested();
                    string target = Path.Combine(dest,name);
                    pending += await CopyAsync(source,target,ct);
                }
                foreach (var file in new DirectoryInfo(dest).EnumerateFiles($"values-????????-ha-{expectedNode}-*.jsonl"))
                {
                    if (ValidName(file.Name,expectedNode) && !sources.ContainsKey(file.Name) &&
                        file.LastWriteTimeUtc < now.UtcDateTime.AddDays(-options.RetentionDays))
                    {
                        file.Delete();
                        _verified.Remove(file.FullName);
                    }
                }
                Volatile.Write(ref _status,new(pending == 0 ? "Synchronized" : "CatchingUp", now, pending,
                    sources.OrderByDescending(x=>x.Value.LastWriteTimeUtc).Select(x=>x.Key).FirstOrDefault()));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (Status.State != "Unavailable") logger.LogWarning(ex,"Cannot synchronize point history from peer.");
                Volatile.Write(ref _status,Status with { State="Unavailable", PendingBytes=null });
            }
        }
    }

    private static bool ValidName(string name,string node)
    {
        if (!name.EndsWith(".jsonl", StringComparison.Ordinal)) return false;
        var parts = name[..^6].Split('-');
        return parts.Length == 6 && parts[0] == "values" && DateOnly.TryParseExact(parts[1],"yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _) &&
            parts[2] == "ha" && parts[3] == node && long.TryParse(parts[4],out var epoch) && epoch > 0 && Guid.TryParseExact(parts[5],"N",out _);
    }

    private async Task<long> CopyAsync(FileInfo source, string target, CancellationToken ct)
    {
        var revision = (source.Length,source.LastWriteTimeUtc.Ticks);
        if (_verified.TryGetValue(target,out var cached) && cached == revision && File.Exists(target)) return 0;
        using var input = new FileStream(source.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true);
        if (source.Extension == ".gz")
        {
            using var expanded = new GZipStream(input,CompressionMode.Decompress);
            await ReplaceVerifiedAsync(expanded,target,ct);
            _verified[target] = revision;
            return 0;
        }
        long length = input.Length;
        using (var output = new FileStream(target,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read|FileShare.Delete,65536,true))
        {
            // Immutable volume names make the durable local length a resumable offset.
            if (output.Length <= length)
            {
                input.Position = output.Position = output.Length;
                int count = (int)Math.Min(_copyBudget, length - output.Length);
                if (count > 0)
                    await AppendVerifiedAsync(input,output,count,ct);
                if (output.Length < length) return length-output.Length;
                if (source.LastWriteTimeUtc > DateTime.UtcNow.AddSeconds(-15)) return 0;
                input.Position=output.Position=0;
                byte[] original = await SHA256.HashDataAsync(input,ct);
                byte[] copied = await SHA256.HashDataAsync(output,ct);
                if (original.AsSpan().SequenceEqual(copied))
                { _verified[target]=revision; return 0; }
            }
        }
        input.Position=0;
        await ReplaceVerifiedAsync(input,target,ct);
        _verified[target]=revision;
        return 0;
    }

    private static async Task AppendVerifiedAsync(Stream input, FileStream output, int count, CancellationToken ct)
    {
        long start=output.Position;
        byte[] buffer=ArrayPool<byte>.Shared.Rent(128*1024);
        try
        {
            using var checksum=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int remaining=count;remaining>0;)
            {
                int size=Math.Min(remaining,buffer.Length);
                await input.ReadExactlyAsync(buffer.AsMemory(0,size),ct);
                checksum.AppendData(buffer,0,size);
                await output.WriteAsync(buffer.AsMemory(0,size),ct);
                remaining-=size;
            }
            output.Flush(true);
            byte[] expected=checksum.GetHashAndReset();
            output.Position=start;
            for (int remaining=count;remaining>0;)
            {
                int size=Math.Min(remaining,buffer.Length);
                await output.ReadExactlyAsync(buffer.AsMemory(0,size),ct);
                checksum.AppendData(buffer,0,size);
                remaining-=size;
            }
            if (!expected.AsSpan().SequenceEqual(checksum.GetHashAndReset())) throw new IOException("History replica verification failed.");
        }
        catch { output.SetLength(start); throw; }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static async Task ReplaceVerifiedAsync(Stream input,string destination,CancellationToken ct)
    {
        string temp=destination+".partial";
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var file = new FileStream(temp, FileMode.Create,FileAccess.ReadWrite,FileShare.None,65536,true))
        {
            var buffer = new byte[131072];
            int count;
            while ((count=await input.ReadAsync(buffer,ct)) != 0)
            {
                hash.AppendData(buffer,0,count);
                await file.WriteAsync(buffer.AsMemory(0,count),ct);
            }
            file.Flush(true);
            file.Position=0;
            byte[] written=await SHA256.HashDataAsync(file,ct);
            if (!written.AsSpan().SequenceEqual(hash.GetHashAndReset())) throw new IOException("History volume checksum mismatch.");
        }
        File.Move(temp,destination,true);
    }
}
