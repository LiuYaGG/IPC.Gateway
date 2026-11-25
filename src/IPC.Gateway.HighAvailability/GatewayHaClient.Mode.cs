using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace IPC.Gateway.HighAvailability;

public sealed partial class GatewayHaClient
{
    private sealed record LocalMode(long Generation, string Role, bool Final);
    private sealed record StoredMode(string ClusterId, string NodeId, LocalMode Mode);
    private volatile LocalMode _localMode = new(-1, "Cluster", false);
    private FileStream? _modeFile;
    private bool _businessPeriod;
    private volatile bool _alarmImportComplete;
    public bool ManagedStandalone => DisasterTakeoverActive || _localMode.Role == "Standalone";
    public bool IsParked => !DisasterTakeoverActive && _localMode.Role == "Parked";

    private void OpenModeState()
    {
        var dir = Directory.CreateDirectory(Path.GetDirectoryName(_latchPath)!);
        // An exclusive descriptor also prevents a second local process from consuming an offline grant.
        _modeFile = new FileStream(Path.Combine(dir.FullName, "gateway-mode.dat"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (OperatingSystem.IsLinux())
        {
            // Make both the new directory and its entries durable before acknowledging enrollment.
            SyncDirectory(dir.FullName);
            if (dir.Parent != null) SyncDirectory(dir.Parent.FullName);
        }
        if (_modeFile.Length == 0) return;
        if (_modeFile.Length > 4096 || _modeFile.Length <= SHA256.HashSizeInBytes)
            throw new InvalidDataException("HA_INVALID_LOCAL_MODE");
        byte[] bytes = new byte[(int)_modeFile.Length];
        _modeFile.ReadExactly(bytes);
        var payload = bytes.AsSpan(SHA256.HashSizeInBytes);
        if (!CryptographicOperations.FixedTimeEquals(bytes.AsSpan(0, SHA256.HashSizeInBytes), SHA256.HashData(payload)))
            throw new InvalidDataException("HA_INVALID_LOCAL_MODE");
        var entry = JsonSerializer.Deserialize<StoredMode>(payload);
        if (entry?.ClusterId != ClusterId || entry.NodeId != NodeId || entry.Mode is not { Generation: >= 0 }
            || entry.Mode.Role is not ("Standalone" or "Parked" or "Cluster") || _latched == 0)
            throw new InvalidDataException("HA_INVALID_LOCAL_MODE");
        _localMode = entry.Mode;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int OpenDirectory(string path, int flags);
    [DllImport("libc", SetLastError = true, EntryPoint = "fsync")]
    private static extern int SyncDescriptor(int fd);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseDescriptor(int fd);
    private static void SyncDirectory(string path)
    {
        int fd = OpenDirectory(path, 0);
        if (fd < 0) throw new IOException("Could not open HA state directory for synchronization.");
        try { if (SyncDescriptor(fd) != 0) throw new IOException("Could not synchronize HA state directory."); }
        finally { CloseDescriptor(fd); }
    }

    // Call under _inflightLock. The file is updated in place; the checksum detects a torn write.
    private void SaveMode(LocalMode next)
    {
        if (_localMode == next) return;
        QuiesceForEnrollment();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new StoredMode(ClusterId, NodeId, next));
        var file = _modeFile ?? throw new InvalidOperationException("HA_NOT_PROVISIONED");
        file.Position = 0;
        file.Write(SHA256.HashData(payload));
        file.Write(payload);
        file.SetLength(file.Position);
        file.Flush(true);
        if (OperatingSystem.IsLinux() && _localMode.Generation == -1)
            SyncDirectory(Path.GetDirectoryName(_latchPath)!);
        if (next.Role != "Cluster" || _localMode.Role != "Cluster") RevokeLocal();
        _localMode = next;
    }

    public bool TryBeginBusinessPeriod(bool clustered, long epoch)
    {
        lock (_inflightLock)
        {
            if (!CanRunBusiness || clustered != IsCluster || Epoch != epoch || _businessPeriod) return false;
            _businessPeriod = true;
            return true;
        }
    }

    public void MarkBusinessPeriodDrained()
    {
        lock (_inflightLock)
        {
            _businessPeriod = false;
            if (_inflight == 0) _disasterDraining = false;
        }
    }

    public void MarkInitialAlarmImportComplete() => _alarmImportComplete = true;

    public async Task ReconcileBusinessModeAsync(JsonElement status, CancellationToken ct)
    {
        if (!status.TryGetProperty("quorumAvailable", out var q) || q.ValueKind != JsonValueKind.True) return;
        StopDisasterTakeover();
        if (!long.TryParse(Text(status, "modeGeneration"), NumberStyles.None, CultureInfo.InvariantCulture, out long generation))
            return; // Older arbiters cannot authorize a return to standalone.
        string? phase = Text(status, "mode");
        bool Contains(string list, string node) => status.TryGetProperty(list, out var value) &&
            value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(n => n.GetString() == node);
        bool enrolled = status.TryGetProperty("enrollmentRequested", out var request) && request.ValueKind == JsonValueKind.True;
        bool firstEnrollment = phase == "Enabled" && enrolled && Text(status, "configVersion") == "0";
        bool parked = Contains("parkedNodes", NodeId);
        bool mustAck = phase is "Disabling" or "Enabling" || (firstEnrollment && !parked);
        lock (_inflightLock)
        {
            var current = _localMode;
            if (generation < current.Generation || !enrolled && generation == 0) return;
            if (firstEnrollment && !parked && !IsCluster &&
                Text(status, "enrollmentNode") == NodeId && !Contains("parkedNodes", NodeId == "A" ? "B" : "A"))
                return; // Keep the initiating singleton running until the peer has durably drained.

            if (mustAck)
            {
                if (generation == current.Generation && current.Final) return;
                SaveMode(new(generation, "Parked", false));
                if (_businessPeriod || _inflight != 0 || (Text(status,"configVersion") == "0" && !_alarmImportComplete)) return;
                // Retry even if this node already acknowledged: the old owner's lease can expire later.
            }
            else if (phase == "Disabled")
            {
                bool designated = Text(status, "standaloneNode") == NodeId;
                if (designated && !ManagedStandalone && (_businessPeriod || _inflight != 0))
                {
                    SaveMode(new(generation, "Parked", false));
                    return;
                }
                SaveMode(new(generation, designated ? "Standalone" : "Parked", true));
                return;
            }
            else if (phase == "Enabled")
            {
                SaveMode(new(generation, "Cluster", true));
                return;
            }
            else return;
        }
        await CommandAsync("/v1/mode-ack", new { clusterId = ClusterId, nodeId = NodeId, instanceId = InstanceId,
            modeGeneration = generation.ToString(CultureInfo.InvariantCulture), requestId = Guid.NewGuid().ToString("N") }, ct);
    }
}
