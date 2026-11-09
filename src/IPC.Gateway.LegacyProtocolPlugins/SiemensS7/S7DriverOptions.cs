using System;
using System.Globalization;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    internal sealed class S7DriverOptions
    {
        public string ControllerProfile { get; private set; } = "Auto";
        public string TsapMode { get; private set; } = "RackSlot";
        public string ConnectionType { get; private set; } = "PG";
        public ushort LocalTsap { get; private set; } = 0x0100;
        public ushort CustomRemoteTsap { get; private set; } = 0x0101;
        public int MaxItemsPerRequest { get; private set; } = 20;

        public ushort ResolveRemoteTsap(int rack, int slot)
        {
            if (string.Equals(TsapMode, "Custom", StringComparison.OrdinalIgnoreCase))
                return CustomRemoteTsap;

            if (rack < 0 || rack > 7)
                throw new ArgumentOutOfRangeException(nameof(rack), "S7 Rack must be between 0 and 7.");
            if (slot < 0 || slot > 31)
                throw new ArgumentOutOfRangeException(nameof(slot), "S7 Slot must be between 0 and 31.");

            int connectionType = ResolveConnectionType(ConnectionType);
            return checked((ushort)((connectionType << 8) | (rack * 0x20) | slot));
        }

        public static S7DriverOptions Parse(PlcConnectionOptions connection)
        {
            S7DriverOptions options = new S7DriverOptions();
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.ControllerProfile = GetString(root, "controllerProfile", options.ControllerProfile);
                options.TsapMode = GetString(root, "s7TsapMode", options.TsapMode);
                options.ConnectionType = GetString(root, "s7ConnectionType", options.ConnectionType);
                options.LocalTsap = GetTsap(root, "s7LocalTsap", options.LocalTsap);
                options.CustomRemoteTsap = GetTsap(root, "s7RemoteTsap", options.CustomRemoteTsap);
                options.MaxItemsPerRequest = GetInt32(root, "s7MaxItemsPerRequest", options.MaxItemsPerRequest, 1, 64);
            }
            catch (JsonException)
            {
                // Keep compatibility defaults when optional driver JSON is malformed.
            }
            return options;
        }

        private static int ResolveConnectionType(string value)
        {
            if (string.Equals(value, "OP", StringComparison.OrdinalIgnoreCase))
                return 0x02;
            if (string.Equals(value, "Basic", StringComparison.OrdinalIgnoreCase))
                return 0x03;
            return 0x01;
        }

        private static string GetString(JsonElement root, string name, string fallback)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static ushort GetTsap(JsonElement root, string name, ushort fallback)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt16(out ushort numeric))
                return numeric;
            if (value.ValueKind != JsonValueKind.String)
                throw new FormatException(name + " must be a 16-bit hexadecimal TSAP value.");

            string text = (value.GetString() ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (text.Length != 4 || !ushort.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort result))
                throw new FormatException(name + " must be a 4-digit hexadecimal TSAP value, for example 0102.");
            return result;
        }

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
                return fallback;
            return Math.Clamp(result, min, max);
        }
    }
}
