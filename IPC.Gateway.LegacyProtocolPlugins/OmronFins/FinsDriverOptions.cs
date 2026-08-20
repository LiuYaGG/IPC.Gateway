using System;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    internal sealed class FinsDriverOptions
    {
        public string ControllerProfile { get; private set; } = "Auto";
        public byte SourceNetwork { get; private set; }
        public byte DestinationNetwork { get; private set; }
        public byte SourceNode { get; private set; }
        public byte DestinationNode { get; private set; }
        public byte SourceUnit { get; private set; }
        public byte DestinationUnit { get; private set; }
        public int MaxWordCount { get; private set; } = 240;
        public int MaxBitCount { get; private set; } = 480;
        public int MaxSparseItems { get; private set; } = 120;
        public int MaxGapWords { get; private set; } = 4;
        public int MaxEmBank { get; private set; } = 0x18;
        public int UdpReadRetries { get; private set; } = 1;

        public bool IsNjNx => ControllerProfile.Equals("NJ/NX", StringComparison.OrdinalIgnoreCase);

        public static FinsDriverOptions Parse(PlcConnectionOptions connection)
        {
            FinsDriverOptions options = new FinsDriverOptions
            {
                DestinationNetwork = checked((byte)Math.Clamp(connection.Rack, 0, 127)),
                DestinationUnit = checked((byte)Math.Clamp(connection.Slot, 0, 254))
            };
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.ControllerProfile = GetString(root, "controllerProfile", options.ControllerProfile);
                options.SourceNetwork = GetByte(root, "sourceNetwork", options.SourceNetwork, 0, 127);
                options.DestinationNetwork = GetByte(root, "network", options.DestinationNetwork, 0, 127);
                options.SourceNode = GetByte(root, "sourceNode", options.SourceNode, 0, 254);
                options.DestinationNode = GetByte(root, "destinationNode", options.DestinationNode, 0, 254);
                options.SourceUnit = GetByte(root, "sourceUnit", options.SourceUnit, 0, 254);
                options.DestinationUnit = GetByte(root, "destinationUnit", options.DestinationUnit, 0, 254);
                options.MaxWordCount = GetInt32(root, "maxWordCount", options.MaxWordCount, 1, 999);
                options.MaxBitCount = GetInt32(root, "maxBitCount", options.MaxBitCount, 1, 1998);
                options.MaxSparseItems = GetInt32(root, "maxSparseItems", options.MaxSparseItems, 1, 167);
                options.MaxGapWords = GetInt32(root, "maxGapWords", options.MaxGapWords, 0, 64);
                options.MaxEmBank = GetInt32(root, "maxEmBank", options.MaxEmBank, 0, 0x18);
                options.UdpReadRetries = GetInt32(root, "udpReadRetries", options.UdpReadRetries, 0, 1);
            }
            catch (JsonException)
            {
                // Keep backward-compatible Rack/Slot defaults when optional JSON is malformed.
            }

            return options;
        }

        private static string GetString(JsonElement root, string name, string fallback)
        {
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty(name, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static byte GetByte(JsonElement root, string name, byte fallback, int min, int max)
        {
            int value = GetInt32(root, name, fallback, min, max);
            return checked((byte)value);
        }

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(name, out JsonElement value) ||
                !value.TryGetInt32(out int result))
                return fallback;
            return Math.Clamp(result, min, max);
        }
    }
}
