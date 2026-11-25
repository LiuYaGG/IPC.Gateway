using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace IPC.Gateway.Arbiter;

public sealed record MemberOptions(string NodeId, Uri ClientUrl, Uri PeerUrl, string CertificateSha256, string? GatewayCertificateSha256 = null,
    string? NextCertificateSha256 = null, string? NextGatewayCertificateSha256 = null)
{
    public bool Matches(string pin, bool peer) => peer
        ? ArbiterOptions.SamePin(CertificateSha256, pin) || ArbiterOptions.SamePin(NextCertificateSha256, pin)
        : ArbiterOptions.SamePin(GatewayCertificateSha256, pin) || ArbiterOptions.SamePin(NextGatewayCertificateSha256, pin);
}

public sealed class ArbiterOptions
{
    public string ClusterId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string StateDirectory { get; set; } = "";
    public string CertificatePath { get; set; } = "";
    public string? CertificatePassword { get; set; }
    public string? CertificatePasswordFile { get; set; }
    public bool AllowInitialize { get; set; }
    public MemberOptions[] Members { get; set; } = [];
    public MemberOptions Self => Members.Single(m => m.NodeId == NodeId);
    public string MembershipFingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        ClusterId, Members = Members.OrderBy(m => m.NodeId, StringComparer.Ordinal)
            .Select(m => new { m.NodeId, m.ClientUrl, m.PeerUrl, business = !string.IsNullOrEmpty(m.GatewayCertificateSha256) })
    })));
    public static string Fingerprint(X509Certificate2 cert) => cert.GetCertHashString(HashAlgorithmName.SHA256);
    public static bool SamePin(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool Current(X509Certificate2 cert) => cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() >= DateTime.UtcNow;

    public FileStream OpenStateDirectory(X509Certificate2 cert)
    {
        static bool Id(string s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 128;
        static bool Address(Uri u) => u is { IsAbsoluteUri: true, Scheme: "https", AbsolutePath: "/", UserInfo: "", Query: "", Fragment: "" };
        if (!Id(ClusterId) || !Id(NodeId) || !Path.IsPathFullyQualified(StateDirectory) || Members.Length != 3)
            throw new InvalidDataException($"A cluster ID, node ID, absolute state directory and three members are required (configured members: {Members.Length}).");
        if (Members.Any(m => !Id(m.NodeId) || !Address(m.ClientUrl) || !Address(m.PeerUrl) || m.ClientUrl.Port == m.PeerUrl.Port) ||
            Members.SelectMany(m => new[] { m.ClientUrl, m.PeerUrl }).Distinct().Count() != 6 ||
            Members.Select(m => m.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("Member names and HTTPS endpoints must be unique.");
        var pins = Members.SelectMany(m => new[] {m.CertificateSha256,m.GatewayCertificateSha256,m.NextCertificateSha256,m.NextGatewayCertificateSha256})
            .Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (!Members.Select(m => m.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(["A", "B", "C"]) ||
            Members.Any(m => string.IsNullOrEmpty(m.CertificateSha256) || (m.NodeId == "C" ?
                !string.IsNullOrEmpty(m.GatewayCertificateSha256) || !string.IsNullOrEmpty(m.NextGatewayCertificateSha256) :
                string.IsNullOrEmpty(m.GatewayCertificateSha256))) ||
            pins.Any(p => p!.Length != 64 || !p.All(Uri.IsHexDigit)) ||
            pins.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pins.Length)
            throw new InvalidDataException("Expected three distinct member and two gateway SHA-256 certificate pins.");
        if (!Self.Matches(Fingerprint(cert), peer: true) || !cert.HasPrivateKey || !Current(cert))
            throw new InvalidDataException("Local certificate is expired, lacks a private key or does not match the member pin.");
        Directory.CreateDirectory(StateDirectory);
        var handle = new FileStream(Path.Combine(StateDirectory, "member.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var path = Path.Combine(StateDirectory, "identity.json");
            // Leaf keys can rotate under operator-controlled configuration. Voter identity is
            // its registered Raft endpoint; changing it in-place would create a different cluster.
            var oldIdentity = JsonSerializer.Serialize(new { ClusterId, NodeId, MembershipFingerprint });
            var identity = JsonSerializer.Serialize(new { ClusterId, NodeId, MembershipFingerprint, controlVersion = TransportMigrationFiles.IdentityControlVersion(StateDirectory) });
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                var raftState = new FileInfo(Path.Combine(StateDirectory, "wal", "state"));
                if (!raftState.Exists || raftState.Length == 0)
                    throw new InvalidDataException("Existing member has lost persistent Raft state. Restore its durable volume before starting.");
                if (existing != identity && existing != oldIdentity)
                {
                    TransportMigrationFiles.MigrateIdentity(this,existing,identity);
                    existing = identity;
                }
                if (existing == oldIdentity)
                {
                    // A pre-switch arbiter would ignore the standalone reservation when
                    // replaying the WAL. Its strict manifest check must refuse this volume.
                    var tmp = path + ".upgrade";
                    using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        file.Write(System.Text.Encoding.UTF8.GetBytes(identity));
                        file.Flush(true);
                    }
                    File.Move(tmp, path, true);
                }
            }
            else
            {
                if (!AllowInitialize || Directory.EnumerateFileSystemEntries(StateDirectory).Any(p => Path.GetFileName(p) != "member.lock"))
                    throw new InvalidDataException("Explicit fresh member provisioning is required.");
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(System.Text.Encoding.UTF8.GetBytes(identity));
                file.Flush(true);
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
}

internal sealed class PeerHandlerFactory(ArbiterOptions options, X509Certificate2 cert) : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        handler.ClientCertificates.Add(cert);
        handler.ServerCertificateCustomValidationCallback = (request, server, _, errors) =>
            server != null && ArbiterOptions.Current(server) && request.RequestUri is { } uri &&
            !errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) &&
            options.Members.Any(m => m.PeerUrl.Authority == uri.Authority && m.Matches(ArbiterOptions.Fingerprint(server), peer: true));
        return new ClusterHeader(handler, MembershipPreflight.PeerHeader(options.MembershipFingerprint));
    }

    private sealed class ClusterHeader(HttpMessageHandler handler, string fingerprint) : DelegatingHandler(handler)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-IPC-Raft-Cluster", fingerprint);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
