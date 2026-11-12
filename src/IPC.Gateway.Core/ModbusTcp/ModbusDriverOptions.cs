using System;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.ModbusTcp
{
    public sealed class ModbusDriverOptions
    {
        public int MaxBatchGapPoints { get; private set; } = 2;
        public ModbusDataFormat? DataFormat { get; private set; }

        public ModbusDataFormat ResolveDataFormat(PlcWordOrder legacyWordOrder) =>
            DataFormat ?? (legacyWordOrder == PlcWordOrder.LowWordFirst ? ModbusDataFormat.CDAB : ModbusDataFormat.ABCD);

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
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, "modbusDataFormat", StringComparison.OrdinalIgnoreCase)) continue;
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !Enum.TryParse<ModbusDataFormat>(property.Value.GetString(), true, out var format) || !Enum.IsDefined(format))
                        throw new ArgumentException("Modbus data format must be ABCD, BADC, CDAB, or DCBA.");
                    options.DataFormat = format;
                }
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
