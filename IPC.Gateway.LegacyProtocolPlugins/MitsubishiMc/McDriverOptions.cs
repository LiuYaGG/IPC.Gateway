using System;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.MitsubishiMc
{
    internal sealed class McDriverOptions
    {
        public string FrameType { get; private set; } = "3E";
        public string DataCode { get; private set; } = "Binary";
        public byte NetworkNumber { get; private set; }
        public byte PcNumber { get; private set; } = 0xFF;
        public ushort ModuleIoNumber { get; private set; } = 0x03FF;
        public byte StationNumber { get; private set; }
        public int MaxBatchGapPoints { get; private set; } = 2;

        public static McDriverOptions Parse(PlcConnectionOptions connection)
        {
            McDriverOptions options = new McDriverOptions
            {
                NetworkNumber = checked((byte)Math.Clamp(connection.Rack, 0, 255)),
                StationNumber = checked((byte)Math.Clamp(connection.Slot, 0, 255))
            };
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.FrameType = GetString(root, "mcFrameType", options.FrameType);
                options.DataCode = GetString(root, "mcDataCode", options.DataCode);
                options.NetworkNumber = GetByte(root, "networkNumber", options.NetworkNumber);
                options.PcNumber = GetByte(root, "pcNumber", options.PcNumber);
                options.ModuleIoNumber = GetUInt16(root, "moduleIoNumber", options.ModuleIoNumber);
                options.StationNumber = GetByte(root, "stationNumber", options.StationNumber);
                options.MaxBatchGapPoints = GetInt32(root, "mcMaxBatchGapPoints", options.MaxBatchGapPoints, 0, 64);
            }
            catch (JsonException)
            {
                // Keep legacy Rack/Slot defaults when optional JSON is malformed.
            }
            return options;
        }

        private static string GetString(JsonElement root, string name, string fallback)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static byte GetByte(JsonElement root, string name, byte fallback)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte result) ? result : fallback;
        }

        private static ushort GetUInt16(JsonElement root, string name, ushort fallback)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.TryGetUInt16(out ushort result) ? result : fallback;
        }

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
                ? Math.Clamp(result, min, max)
                : fallback;
        }
    }
}
