using System.Collections.Generic;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration;

public static class DeviceConfigComparer
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    public static bool IsSameDeviceUpdate(DeviceConfig? current, DeviceConfig? input)
    {
        if (current == null || input == null)
            return false;

        string currentFingerprint = CreateDeviceUpdateFingerprint(current.Protocol, current);
        string inputFingerprint = CreateDeviceUpdateFingerprint(input.Protocol, input);
        return string.Equals(currentFingerprint, inputFingerprint, StringComparison.Ordinal);
    }

    public static bool CanReuseRuntimeState(DeviceConfig? previous, DeviceConfig? current)
    {
        if (previous == null || current == null)
            return false;

        if (!HasSameDeviceId(previous, current))
            return false;

        return string.Equals(
            CreateRuntimeReuseFingerprint(previous, includeEnabled: true),
            CreateRuntimeReuseFingerprint(current, includeEnabled: true),
            StringComparison.Ordinal);
    }

    public static bool CanReuseRuntimeStateForEnabledChange(DeviceConfig? previous, DeviceConfig? current)
    {
        if (previous == null || current == null)
            return false;

        if (!HasSameDeviceId(previous, current) || previous.Enabled == current.Enabled)
            return false;

        return string.Equals(
            CreateRuntimeReuseFingerprint(previous, includeEnabled: false),
            CreateRuntimeReuseFingerprint(current, includeEnabled: false),
            StringComparison.Ordinal);
    }

    private static bool HasSameDeviceId(DeviceConfig previous, DeviceConfig current)
    {
        return !string.IsNullOrWhiteSpace(previous.Id) &&
               !string.IsNullOrWhiteSpace(current.Id) &&
               string.Equals(previous.Id, current.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRuntimeReuseFingerprint(DeviceConfig device, bool includeEnabled)
    {
        DeviceConfig normalized = ProjectConfigCloner.Clone(new ProjectConfig
        {
            Devices = new List<DeviceConfig> { device }
        }).Devices[0];

        DeviceRuntimeReuseProjection projection = new DeviceRuntimeReuseProjection
        {
            Id = normalized.Id ?? string.Empty,
            Protocol = normalized.Protocol,
            Enabled = includeEnabled ? normalized.Enabled : null,
            Connection = NormalizeConnection(normalized.Protocol, normalized.Connection),
            DefaultScanRateMs = normalized.DefaultScanRateMs,
            FailureRetryDelayMs = normalized.FailureRetryDelayMs,
            MaxFailureRetryDelayMs = normalized.MaxFailureRetryDelayMs
        };

        return JsonSerializer.Serialize(projection, JsonOptions);
    }

    private static string CreateDeviceUpdateFingerprint(PlcProtocol protocol, DeviceConfig device)
    {
        PlcConnectionOptions connection = NormalizeConnection(protocol, device.Connection);
        int failureRetryDelayMs = NormalizePositive(device.FailureRetryDelayMs, 1000);
        int maxFailureRetryDelayMs = NormalizePositive(device.MaxFailureRetryDelayMs, 30000);
        if (maxFailureRetryDelayMs < failureRetryDelayMs)
            maxFailureRetryDelayMs = failureRetryDelayMs;

        DeviceUpdateProjection projection = new DeviceUpdateProjection
        {
            Name = device.Name ?? string.Empty,
            Enabled = device.Enabled,
            Protocol = protocol,
            Connection = connection,
            DefaultScanRateMs = NormalizePositive(device.DefaultScanRateMs, 1000),
            FailureRetryDelayMs = failureRetryDelayMs,
            MaxFailureRetryDelayMs = maxFailureRetryDelayMs
        };

        return JsonSerializer.Serialize(projection, JsonOptions);
    }

    private static PlcConnectionOptions NormalizeConnection(PlcProtocol protocol, PlcConnectionOptions? source)
    {
        PlcConnectionOptions connection = source == null ? new PlcConnectionOptions() : new PlcConnectionOptions
        {
            Protocol = source.Protocol,
            Host = source.Host ?? string.Empty,
            Port = source.Port,
            Rack = source.Rack,
            Slot = source.Slot,
            TimeoutMilliseconds = source.TimeoutMilliseconds,
            WordOrder = source.WordOrder,
            Transport = source.Transport,
            DataBits = source.DataBits,
            SerialParity = source.SerialParity,
            SerialStopBits = source.SerialStopBits,
            Username = source.Username ?? string.Empty,
            Password = source.Password ?? string.Empty,
            CertificatePath = source.CertificatePath ?? string.Empty,
            CertificatePassword = source.CertificatePassword ?? string.Empty,
            CertificateThumbprint = source.CertificateThumbprint ?? string.Empty,
            TrustStorePath = source.TrustStorePath ?? string.Empty,
            ValidateServerCertificate = source.ValidateServerCertificate,
            OpcDaServerProgId = source.OpcDaServerProgId ?? string.Empty,
            OpcDaGroupName = source.OpcDaGroupName ?? string.Empty,
            DriverId = source.DriverId ?? string.Empty,
            DriverOptionsJson = source.DriverOptionsJson ?? string.Empty
        };

        connection.Protocol = protocol;
        return connection;
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value <= 0 ? fallback : value;
    }

    private sealed class DeviceUpdateProjection
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public PlcProtocol Protocol { get; set; }
        public PlcConnectionOptions Connection { get; set; } = new PlcConnectionOptions();
        public int DefaultScanRateMs { get; set; }
        public int FailureRetryDelayMs { get; set; }
        public int MaxFailureRetryDelayMs { get; set; }
    }

    private sealed class DeviceRuntimeReuseProjection
    {
        public string Id { get; set; } = string.Empty;
        public PlcProtocol Protocol { get; set; }
        public bool? Enabled { get; set; }
        public PlcConnectionOptions Connection { get; set; } = new PlcConnectionOptions();
        public int DefaultScanRateMs { get; set; }
        public int FailureRetryDelayMs { get; set; }
        public int MaxFailureRetryDelayMs { get; set; }
    }
}
