using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    internal static class LegacyPluginLicense
    {
        private const string ProductId = "IPC.Gateway";
        private const string AllDriversFeature = "commercial-drivers";
        private static readonly object SyncRoot = new object();
        private static DateTime _nextValidationUtc;
        private static LicenseValidationResult _cached = LicenseValidationResult.Invalid("Commercial driver license has not been validated.");

        public static void EnsureDriverAllowed(string driverId)
        {
            LicenseValidationResult status = GetStatus();
            if (!status.Valid)
                throw new InvalidOperationException("Commercial driver license is not valid. " + status.Message);
            if (!IsDriverAllowed(status.Features, driverId))
                throw new InvalidOperationException("Commercial driver license does not include driver: " + driverId + ".");
        }

        private static LicenseValidationResult GetStatus()
        {
            DateTime now = DateTime.UtcNow;
            lock (SyncRoot)
            {
                if (now < _nextValidationUtc)
                    return _cached;
                _cached = Validate(now);
                _nextValidationUtc = now.AddSeconds(5);
                return _cached;
            }
        }

        private static LicenseValidationResult Validate(DateTime nowUtc)
        {
            string baseDirectory = AppContext.BaseDirectory;
            string licensePath = Environment.GetEnvironmentVariable("IPC_GATEWAY_COMMERCIAL_LICENSE_FILE") ??
                                 Path.Combine(baseDirectory, "Data", "License", "ipc-gateway-license.json");
            string publicKeyPath = Environment.GetEnvironmentVariable("IPC_GATEWAY_COMMERCIAL_PUBLIC_KEY_FILE") ??
                                   Path.Combine(baseDirectory, "Data", "License", "ipc-gateway-license-public.pem");
            if (!File.Exists(licensePath))
                return LicenseValidationResult.Invalid("License file is missing.");
            if (!File.Exists(publicKeyPath))
                return LicenseValidationResult.Invalid("Trusted public key is missing.");

            try
            {
                LicensePayload payload = JsonSerializer.Deserialize<LicensePayload>(
                    File.ReadAllText(licensePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LicensePayload();
                if (!string.Equals(payload.ProductId, ProductId, StringComparison.OrdinalIgnoreCase))
                    return LicenseValidationResult.Invalid("License product does not match the commercial driver pack.");
                if (payload.IssuedUtc != DateTime.MinValue && payload.IssuedUtc.ToUniversalTime() > nowUtc.AddMinutes(10))
                    return LicenseValidationResult.Invalid("License issue time is in the future.");
                if (payload.ExpiresUtc != DateTime.MinValue && payload.IssuedUtc != DateTime.MinValue && payload.ExpiresUtc <= payload.IssuedUtc)
                    return LicenseValidationResult.Invalid("License expiration time must be later than its issue time.");
                if (payload.ExpiresUtc != DateTime.MinValue && payload.ExpiresUtc.ToUniversalTime() < nowUtc)
                    return LicenseValidationResult.Invalid("License is expired.");
                if (string.IsNullOrWhiteSpace(payload.Signature))
                    return LicenseValidationResult.Invalid("License signature is missing.");

                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
                byte[] data = Encoding.UTF8.GetBytes(BuildSignaturePayload(payload));
                byte[] signature = Convert.FromBase64String(payload.Signature.Trim());
                RSASignaturePadding padding = payload.SchemaVersion <= 1 ? RSASignaturePadding.Pkcs1 : RSASignaturePadding.Pss;
                if (!rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, padding))
                    return LicenseValidationResult.Invalid("License signature verification failed.");

                string expectedMachineCode = CreateMachineCode(ProductId);
                if (!string.Equals(NormalizeMachineCode(payload.MachineCode), expectedMachineCode, StringComparison.Ordinal))
                    return LicenseValidationResult.Invalid("License is bound to a different machine.");
                return LicenseValidationResult.Success(payload.Features ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                return LicenseValidationResult.Invalid("License validation failed: " + ex.Message);
            }
        }

        private static bool IsDriverAllowed(IList<string> features, string driverId)
        {
            List<string> normalized = (features ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList();
            if (normalized.Any(item => string.Equals(item, AllDriversFeature, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(item, driverId, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(item, "driver:" + driverId, StringComparison.OrdinalIgnoreCase)))
                return true;

            bool hasDriverEntitlement = normalized.Any(item =>
                item.StartsWith("driver:", StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith("legacy.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item, AllDriversFeature, StringComparison.OrdinalIgnoreCase));
            return !hasDriverEntitlement;
        }

        private static string BuildSignaturePayload(LicensePayload payload)
        {
            string featureText = string.Join(",", (payload.Features ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
            if (payload.SchemaVersion <= 1)
            {
                return string.Join("\n",
                    payload.ProductId ?? string.Empty,
                    payload.CustomerName ?? string.Empty,
                    payload.Edition ?? string.Empty,
                    payload.SerialNumber ?? string.Empty,
                    ToUtcText(payload.IssuedUtc),
                    ToUtcText(payload.ExpiresUtc),
                    payload.MaxDevices.ToString(CultureInfo.InvariantCulture),
                    payload.MaxTags.ToString(CultureInfo.InvariantCulture),
                    featureText);
            }

            return string.Join("\n",
                "IPC.Gateway.License/v2",
                payload.ProductId ?? string.Empty,
                NormalizeMachineCode(payload.MachineCode),
                payload.CustomerName ?? string.Empty,
                payload.Edition ?? string.Empty,
                payload.SerialNumber ?? string.Empty,
                ToUtcText(payload.IssuedUtc),
                ToUtcText(payload.ExpiresUtc),
                payload.MaxDevices.ToString(CultureInfo.InvariantCulture),
                payload.MaxTags.ToString(CultureInfo.InvariantCulture),
                featureText);
        }

        private static string CreateMachineCode(string productId)
        {
            List<string> identifiers = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                AddIfPresent(identifiers, "machine-guid", ReadWindowsMachineGuid());
                AddIfPresent(identifiers, "system-volume", ReadWindowsSystemVolumeSerial());
            }
            else
            {
                AddIfPresent(identifiers, "machine-id", ReadFirstFile("/etc/machine-id", "/var/lib/dbus/machine-id"));
                AddIfPresent(identifiers, "product-uuid", ReadFirstFile("/sys/class/dmi/id/product_uuid"));
                AddIfPresent(identifiers, "board-serial", ReadFirstFile("/sys/class/dmi/id/board_serial"));
            }
            if (identifiers.Count == 0)
                identifiers.Add("fallback=" + Environment.MachineName + "|" + RuntimeInformation.OSArchitecture);
            identifiers.Sort(StringComparer.Ordinal);
            string source = (productId ?? string.Empty).Trim().ToUpperInvariant() + "\n" + string.Join("\n", identifiers);
            string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).Substring(0, 32);
            return "IPC-" + string.Join("-", Enumerable.Range(0, 4).Select(index => hex.Substring(index * 8, 8)));
        }

        private static string NormalizeMachineCode(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static string ToUtcText(DateTime value) => value == DateTime.MinValue ? DateTime.MinValue.ToString("O") : value.ToUniversalTime().ToString("O");

        private static void AddIfPresent(ICollection<string> values, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(name + "=" + value.Trim().ToUpperInvariant());
        }

        private static string ReadWindowsMachineGuid()
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
                return Convert.ToString(key == null ? null : key.GetValue("MachineGuid")) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string ReadWindowsSystemVolumeSerial()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                return GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0) ? serial.ToString("X8") : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string ReadFirstFile(params string[] paths)
        {
            foreach (string path in paths)
            {
                try { if (File.Exists(path)) return File.ReadAllText(path).Trim(); }
                catch { }
            }
            return string.Empty;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(string rootPathName, StringBuilder volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder fileSystemNameBuffer, int fileSystemNameSize);

        private sealed class LicensePayload
        {
            public int SchemaVersion { get; set; } = 1;
            public string ProductId { get; set; } = string.Empty;
            public string MachineCode { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public string Edition { get; set; } = string.Empty;
            public string SerialNumber { get; set; } = string.Empty;
            public DateTime IssuedUtc { get; set; }
            public DateTime ExpiresUtc { get; set; }
            public int MaxDevices { get; set; }
            public int MaxTags { get; set; }
            public IList<string> Features { get; set; } = new List<string>();
            public string Signature { get; set; } = string.Empty;
        }

        private sealed class LicenseValidationResult
        {
            public bool Valid { get; private set; }
            public string Message { get; private set; } = string.Empty;
            public IList<string> Features { get; private set; } = new List<string>();
            public static LicenseValidationResult Success(IList<string> features) => new LicenseValidationResult { Valid = true, Message = "License is valid.", Features = features.ToList() };
            public static LicenseValidationResult Invalid(string message) => new LicenseValidationResult { Valid = false, Message = message ?? string.Empty };
        }
    }
}
