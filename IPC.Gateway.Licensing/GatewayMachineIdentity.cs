using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace IPC.Gateway.Licensing;

public static class GatewayMachineIdentity
{
    public static string CreateMachineCode(string productId, string? machineIdOverride = null)
    {
        List<string> identifiers = new();
        if (!string.IsNullOrWhiteSpace(machineIdOverride))
        {
            identifiers.Add("override=" + machineIdOverride.Trim());
        }
        else if (OperatingSystem.IsWindows())
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
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..32];
        return "IPC-" + string.Join("-", Enumerable.Range(0, 4).Select(index => hex.Substring(index * 8, 8)));
    }

    public static string NormalizeMachineCode(string? machineCode)
    {
        return (machineCode ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static void AddIfPresent(ICollection<string> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(name + "=" + value.Trim().ToUpperInvariant());
    }

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsMachineGuid()
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            return Convert.ToString(key?.GetValue("MachineGuid")) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsSystemVolumeSerial()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0)
                ? serial.ToString("X8")
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadFirstFile(params string[] paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch
            {
                // Try the next stable host identifier.
            }
        }
        return string.Empty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
