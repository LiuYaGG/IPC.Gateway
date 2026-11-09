using System;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Dnp3
{
    internal sealed class Dnp3DriverOptions
    {
        public ushort LocalAddress { get; private set; } = 1;
        public ushort RemoteAddress { get; private set; } = 1024;
        public int ScanGapLimit { get; private set; } = 32;
        public bool SelectBeforeOperate { get; private set; } = true;
        public bool StartupIntegrity { get; private set; } = true;
        public bool EnableUnsolicited { get; private set; } = true;
        public int EventScanIntervalSeconds { get; private set; } = 5;
        public int IntegrityScanIntervalSeconds { get; private set; } = 900;
        public int CacheMaxAgeMilliseconds { get; private set; }
        public string TimeSyncMode { get; private set; } = "None";

        public static Dnp3DriverOptions Parse(PlcConnectionOptions connection)
        {
            Dnp3DriverOptions options = new Dnp3DriverOptions();
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;
            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.LocalAddress = GetUInt16(root, "dnp3LocalAddress", options.LocalAddress);
                options.RemoteAddress = GetUInt16(root, "dnp3RemoteAddress", options.RemoteAddress);
                options.ScanGapLimit = GetInt32(root, "dnp3ScanGapLimit", options.ScanGapLimit, 0, 1000);
                options.SelectBeforeOperate = GetBoolean(root, "dnp3SelectBeforeOperate", options.SelectBeforeOperate);
                options.StartupIntegrity = GetBoolean(root, "dnp3StartupIntegrity", options.StartupIntegrity);
                options.EnableUnsolicited = GetBoolean(root, "dnp3EnableUnsolicited", options.EnableUnsolicited);
                options.EventScanIntervalSeconds = GetInt32(root, "dnp3EventScanIntervalSeconds", options.EventScanIntervalSeconds, 0, 3600);
                options.IntegrityScanIntervalSeconds = GetInt32(root, "dnp3IntegrityScanIntervalSeconds", options.IntegrityScanIntervalSeconds, 0, 86400);
                options.CacheMaxAgeMilliseconds = GetInt32(root, "dnp3CacheMaxAgeMilliseconds", options.CacheMaxAgeMilliseconds, 0, 86400000);
                options.TimeSyncMode = GetString(root, "dnp3TimeSyncMode", options.TimeSyncMode);
            }
            catch (JsonException)
            {
            }
            return options;
        }

        private static ushort GetUInt16(JsonElement root, string name, ushort fallback)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.TryGetUInt16(out ushort parsed)
                ? parsed : fallback;

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed)
                ? Math.Clamp(parsed, min, max) : fallback;

        private static bool GetBoolean(JsonElement root, string name, bool fallback)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean() : fallback;

        private static string GetString(JsonElement root, string name, string fallback)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback : fallback;
    }
}
