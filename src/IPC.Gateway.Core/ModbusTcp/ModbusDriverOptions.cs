using System;
using System.Text.Json;

namespace IPC.Plc.Communication.ModbusTcp
{
    public sealed class ModbusDriverOptions
    {
        public int MaxBatchGapPoints { get; private set; } = 2;

        public static ModbusDriverOptions Parse(string? json)
        {
            ModbusDriverOptions options = new ModbusDriverOptions();
            if (string.IsNullOrWhiteSpace(json))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (TryGetInt(document.RootElement, "maxBatchGapPoints", out int value))
                    options.MaxBatchGapPoints = Math.Clamp(value, 0, 64);
            }
            catch (JsonException)
            {
            }

            return options;
        }

        private static bool TryGetInt(JsonElement root, string name, out int value)
        {
            value = 0;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out value);
            }
            return false;
        }
    }
}
