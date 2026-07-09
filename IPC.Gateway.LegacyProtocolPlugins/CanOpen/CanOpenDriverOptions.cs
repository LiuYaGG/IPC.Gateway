using System;
using System.Globalization;
using System.Text.Json;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class CanOpenDriverOptions
    {
        public int CanBitRate { get; private set; } = 500000;
        public int DefaultNodeId { get; private set; } = 1;
        public int MaxBatchItems { get; private set; } = 32;

        public static CanOpenDriverOptions Parse(string json)
        {
            CanOpenDriverOptions options = new CanOpenDriverOptions();
            if (string.IsNullOrWhiteSpace(json))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                options.CanBitRate = ReadInt(root, "canBitRate", options.CanBitRate, 10000, 1000000);
                options.DefaultNodeId = ReadInt(root, "defaultNodeId", options.DefaultNodeId, 1, 127);
                options.MaxBatchItems = ReadInt(root, "maxBatchItems", options.MaxBatchItems, 1, 256);
            }
            catch (JsonException)
            {
            }

            return options;
        }

        private static int ReadInt(JsonElement root, string name, int fallback, int min, int max)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value))
                return fallback;

            int number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
                return Math.Min(max, Math.Max(min, number));
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return Math.Min(max, Math.Max(min, number));

            return fallback;
        }
    }
}
