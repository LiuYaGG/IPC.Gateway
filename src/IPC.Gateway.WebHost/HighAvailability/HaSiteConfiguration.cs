using System.Text.Json;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed record HaMember(string NodeId, string ClientUrl, string PeerUrl, string? GatewayUrl, string CertificateRef);
internal sealed record HaSiteConfiguration(string ClusterId, string Name, string BusinessUrl,
    string PreferredNode, int RenewSeconds, int LeaseSeconds, HaMember[] Members)
{
    public void Validate()
    {
        static bool Label(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
        static bool Url(string? value, bool tls, bool origin = false) => value?.Length <= 1024 &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || (!tls && uri.Scheme == "http")) &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && (!origin || uri.AbsolutePath == "/");
        if (!Label(ClusterId, 128) || !Label(Name, 128) || !Url(BusinessUrl, false) ||
            PreferredNode is not ("A" or "B") || RenewSeconds is < 1 or > 30 ||
            LeaseSeconds < 3 * RenewSeconds || LeaseSeconds > 120)
            throw new HaManagementError(400, "HA_INVALID_CONFIGURATION", "请检查集群身份、业务地址和租约设置。");
        if (Members == null || Members.Length != 3 || Members.Any(x => x is null) ||
            !Members.Select(m => m.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(["A", "B", "C"]))
            throw new HaManagementError(400, "HA_INVALID_MEMBERS", "需要唯一的 A、B、C 三个节点。");
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in Members)
        {
            if (!Url(member.ClientUrl, true, true) || !Url(member.PeerUrl, true, true) || !Label(member.CertificateRef, 256) ||
                (member.NodeId == "C" ? !string.IsNullOrWhiteSpace(member.GatewayUrl) : !Url(member.GatewayUrl, false, true)) ||
                !addresses.Add(new Uri(member.ClientUrl).GetLeftPart(UriPartial.Authority)) ||
                !addresses.Add(new Uri(member.PeerUrl).GetLeftPart(UriPartial.Authority)))
                throw new HaManagementError(400, "HA_INVALID_MEMBER", $"请检查 {member.NodeId} 的地址、端口和证书引用。");
        }
    }
}

internal sealed class HaManagementError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal sealed record DraftRequest(HaSiteConfiguration Config, long ExpectedDraftVersion);
internal sealed record PrecheckRequest(long DraftVersion);
internal sealed record ApplyRequest(long DraftVersion, string PrecheckId, long ExpectedRevision, string OperationId);
internal sealed record SwitchRequest(string Target, string ExpectedEpoch, string? OperationId, string? Reason);
internal sealed record MaintenanceRequest(string NodeId, bool Enabled, string ExpectedEpoch, string OperationId, string? Reason);
internal sealed record ClusterSwitchRequest(string OperationId, bool Enabled, string ExpectedGeneration, string? StandaloneNode);
internal sealed record ClusterSwitchSnapshot(string Phase, string Generation, string? StandaloneNode, bool Supported, string? OperationId, string? Message);
internal sealed record HaCheck(string NodeId, bool Passed, string Message, string[]? Details = null);
internal sealed record HaChange(string OperationId, string Status, string Message, long DraftVersion, long ExpectedRevision,
    string? Actor = null, DateTimeOffset? SubmittedAt = null, string ModeGeneration = "0");
internal sealed record HaMemberProgress(string? NodeId,long? ConfigVersion,string? CommitIndex,string State);
internal sealed record HaLocalState(long DraftVersion = 0, HaSiteConfiguration? Draft = null, string? PrecheckId = null,
    DateTimeOffset? CheckedAt = null, HaChange? LastChange = null, long LastRevision = 0, HaSiteConfiguration? LastActive = null,
    string? TransportPlanId = null);

internal sealed class HaDraftStore(IConfiguration configuration, IHostEnvironment host)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(configuration["Gateway:HighAvailability:ManagementDirectory"] ??
        configuration["Gateway:HighAvailability:StateDirectory"] ?? Path.Combine(host.ContentRootPath, "Data", "HighAvailability"),
        AppContext.BaseDirectory), "management.json");
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public HaLocalState Read()
    {
        if (!File.Exists(_path)) return new();
        using var file = new FileStream(_path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        return JsonSerializer.Deserialize<HaLocalState>(file, Json) ?? throw new InvalidDataException("Invalid HA state.");
    }

    public void Save(HaLocalState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, state, Json);
                file.Flush(flushToDisk: true);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
