using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Infrastructure;

public static partial class PlcDriverPluginRegistry
{
    private sealed class UpgradeTrust
    {
        public string AssemblyPath { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
        public string PublicKeySha256 { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string PackageSha256 { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>Called only after authorized package validation and extraction.</summary>
    public static int PreserveUpgradeTrust(string payloadDirectory, string installDirectory, string packageId, string packageSha256, string? previousDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageSha256.Length != 64)
            throw new ArgumentException("升级包身份或摘要无效。");
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(_trustStorePath) || string.IsNullOrWhiteSpace(_trustedPublicKeyPem)) return 0;
            string install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
            string payload = Path.TrimEndingDirectorySeparator(Path.GetFullPath(payloadDirectory));
            string previous = previousDirectory == null ? install : Path.TrimEndingDirectorySeparator(Path.GetFullPath(previousDirectory));
            var receipts = ReadUpgradeTrust();
            int count = 0;
            IEnumerable<string> oldPaths = previousDirectory == null ? Plugins.Values.Where(p => !p.BuiltIn).Select(p => p.AssemblyPath) :
                new[] { Path.Combine(previous, "Drivers"), Path.Combine(previous, "Plugins", "Drivers") }
                    .Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.dll")).Where(p => !string.IsNullOrEmpty(ResolveManifestPath(p)));
            foreach (string oldPath in oldPaths.Distinct())
            {
                string relative = Path.GetRelativePath(previous, oldPath);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) continue;
                string nextPath = Path.Combine(payload, relative);
                if (!File.Exists(nextPath)) continue;
                var oldManifest = LoadManifest(ResolveManifestPath(oldPath));
                if (!ValidateSignature(oldPath, oldManifest, out _, out _, out string status) ||
                    (status != "Trusted" && status != "UpgradeTrusted")) continue;
                string nextManifestPath = ResolveManifestPath(nextPath);
                if (string.IsNullOrEmpty(nextManifestPath)) continue;
                var nextManifest = LoadManifest(nextManifestPath);
                if (string.IsNullOrWhiteSpace(oldManifest.DriverId) || oldManifest.DriverId != nextManifest.DriverId) continue;
                string digest = ComputeSha256(nextPath);
                if (!string.IsNullOrWhiteSpace(nextManifest.AssemblySha256) && NormalizeHex(nextManifest.AssemblySha256) != digest)
                    throw new InvalidOperationException("升级驱动摘要与清单不一致。");
                string target = Path.GetFullPath(Path.Combine(install, relative));
                string payloadHash = UpgradePayloadHash(nextManifest, digest);
                string publicHash = PublicKeyHash();
                receipts.RemoveAll(r => r.AssemblyPath == target && r.PayloadSha256 == payloadHash);
                receipts.Add(new UpgradeTrust { AssemblyPath = target, PayloadSha256 = payloadHash, PublicKeySha256 = publicHash,
                    PackageId = packageId, PackageSha256 = packageSha256, CreatedUtc = DateTime.UtcNow });
                count++;
            }
            if (count > 0)
            {
                WriteSigningFile(_trustStorePath + ".upgrades.json", JsonSerializer.Serialize(receipts));
                IPC.IpcLogService.WriteInfo($"DriverUpgradeTrust package={packageId} count={count} sha256={packageSha256}");
            }
            return count;
        }
    }

    private static List<UpgradeTrust> ReadUpgradeTrust()
    {
        string path = _trustStorePath + ".upgrades.json";
        if (string.IsNullOrWhiteSpace(_trustStorePath) || !File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<UpgradeTrust>>(File.ReadAllText(path)) ?? new(); }
        catch (JsonException) { return new(); } // Fail closed: an invalid receipt grants no trust.
    }

    private static string PublicKeyHash()
    {
        using RSA rsa = RSA.Create(); rsa.ImportFromPem(_trustedPublicKeyPem);
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
    }

    private static string UpgradePayloadHash(PlcDriverPluginManifest manifest, string digest) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BuildSignaturePayload(manifest, digest))));

    private static bool HasUpgradeTrust(string path, PlcDriverPluginManifest manifest, string digest)
    {
        if (string.IsNullOrWhiteSpace(_trustedPublicKeyPem)) return false;
        string payloadHash = UpgradePayloadHash(manifest, digest);
        string publicHash = PublicKeyHash();
        return ReadUpgradeTrust().Any(r => r.AssemblyPath == Path.GetFullPath(path) && r.PayloadSha256 == payloadHash &&
            r.PublicKeySha256 == publicHash && r.PackageSha256.Length == 64 && !string.IsNullOrWhiteSpace(r.PackageId));
    }
}
