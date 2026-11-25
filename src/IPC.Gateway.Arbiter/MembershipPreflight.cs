using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace IPC.Gateway.Arbiter;

internal sealed record PeerIdentity(string ClusterId, string NodeId, string MembershipFingerprint, int WireVersion, string SoftwareVersion,int TransportVersion=0);
internal sealed record PeerCheck(string NodeId, bool Passed, long ElapsedMs, string? ReasonCode, string? SoftwareVersion);

internal sealed class MembershipPreflight(ArbiterOptions options, IHttpMessageHandlerFactory factory, X509Certificate2 certificate, OwnershipState state) : IDisposable
{
    internal const int WireVersion = 2;
    internal static string PeerHeader(string membership) => membership + "/control-v2";
    internal static readonly string SoftwareVersion = typeof(MembershipPreflight).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private readonly HttpClient client = new(factory.CreateHandler("member-preflight")) { Timeout = TimeSpan.FromSeconds(2) };
    private readonly object sync = new();
    private Task<bool>? diskProbe;

    internal async Task<bool> SupportsTransportAsync(CancellationToken token)
    {
        var ready=await Task.WhenAll(options.Members.Where(m=>m.NodeId!=options.NodeId).Select(async member=>{
            try {
                using var response=await client.GetAsync(new Uri(member.PeerUrl,"/v1/peer-health"),token);
                if(!response.IsSuccessStatusCode)return false;
                var identity=await response.Content.ReadFromJsonAsync<PeerIdentity>(token);
                return identity?.NodeId==member.NodeId && identity.ClusterId==options.ClusterId && identity.MembershipFingerprint==options.MembershipFingerprint && identity.TransportVersion>=1;
            } catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException){return false;}
        }));
        return ready.All(x=>x);
    }

    internal async Task<object> CheckAsync(CancellationToken token)
    {
        var remote = Task.WhenAll(options.Members.Where(m => m.NodeId != options.NodeId).Select(m => ProbePeerAsync(m, token)));
        Task<bool> disk;
        lock (sync)
        {
            // Never pile up fsync requests if the underlying volume stops responding.
            if (diskProbe == null || diskProbe.IsCompleted) diskProbe = Task.Run(CheckDisk);
            disk = diskProbe;
        }
        bool writable;
        try { writable = await disk.WaitAsync(TimeSpan.FromSeconds(2), token); }
        catch (TimeoutException) { writable = false; }
        var checks = await remote;
        var progress = state.LocalProgress();
        bool ready = writable && checks.All(c => c.Passed);
        return new {
            options.ClusterId, options.NodeId, options.Self.ClientUrl, options.Self.PeerUrl,
            certificateSha256 = ArbiterOptions.Fingerprint(certificate), wireVersion = WireVersion, softwareVersion = SoftwareVersion,
            progress.ConfigVersion, progress.CommitIndex, members = options.Members.Select(m => new { m.NodeId, m.ClientUrl, m.PeerUrl, m.CertificateSha256 }),
            ready, storageWritable = writable, checks, reasonCode = !writable ? "HA_STORAGE_UNAVAILABLE" : ready ? null : "HA_PEER_CHECK_FAILED"
        };
    }

    private bool CheckDisk()
    {
        string path = Path.Combine(options.StateDirectory, ".preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            fs.WriteByte(1);
            fs.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private async Task<PeerCheck> ProbePeerAsync(MemberOptions expected, CancellationToken token)
    {
        var time = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(new Uri(expected.PeerUrl, "/v1/peer-health"), token);
            if (!response.IsSuccessStatusCode) return new(expected.NodeId, false, time.ElapsedMilliseconds, "HA_PEER_REJECTED", null);
            var identity = await response.Content.ReadFromJsonAsync<PeerIdentity>(token);
            bool matched = identity != null && identity.NodeId == expected.NodeId && identity.ClusterId == options.ClusterId &&
                identity.MembershipFingerprint == options.MembershipFingerprint && identity.WireVersion == WireVersion;
            return new(expected.NodeId, matched, time.ElapsedMilliseconds, matched ? null : "HA_PEER_IDENTITY_MISMATCH", identity?.SoftwareVersion);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            return new(expected.NodeId, false, time.ElapsedMilliseconds, "HA_PEER_UNREACHABLE", null);
        }
    }

    public void Dispose() => client.Dispose();
}
