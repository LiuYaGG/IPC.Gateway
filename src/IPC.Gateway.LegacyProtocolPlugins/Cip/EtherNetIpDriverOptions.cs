using System;
using System.Globalization;
using System.Text.Json;

namespace IPC.Plc.Communication.Cip
{
    internal sealed class EtherNetIpDriverOptions
    {
        public string IoMode { get; private set; } = "Explicit";
        public byte OutputAssembly { get; private set; } = 100;
        public byte InputAssembly { get; private set; } = 101;
        public byte ConfigurationAssembly { get; private set; } = 1;
        public ushort OutputLength { get; private set; }
        public ushort InputLength { get; private set; }
        public int RequestedPacketIntervalMilliseconds { get; private set; } = 100;
        public string OutputRealTimeFormat { get; private set; } = "Header32Bit";
        public string InputRealTimeFormat { get; private set; } = "Modeless";
        public string InputConnectionType { get; private set; } = "PointToPoint";
        public int InputDataOffset { get; private set; } = 8;
        public int OutputDataOffset { get; private set; }
        public int IoStaleTimeoutMilliseconds { get; private set; } = 1000;

        public bool UsesImplicitIo => IoMode.Equals("Implicit", StringComparison.OrdinalIgnoreCase);

        public static EtherNetIpDriverOptions Parse(string json)
        {
            EtherNetIpDriverOptions options = new EtherNetIpDriverOptions();
            if (string.IsNullOrWhiteSpace(json))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                options.IoMode = ReadString(root, "eipIoMode", options.IoMode);
                options.OutputAssembly = (byte)ReadInt(root, "eipOutputAssembly", options.OutputAssembly, 1, byte.MaxValue);
                options.InputAssembly = (byte)ReadInt(root, "eipInputAssembly", options.InputAssembly, 1, byte.MaxValue);
                options.ConfigurationAssembly = (byte)ReadInt(root, "eipConfigurationAssembly", options.ConfigurationAssembly, 1, byte.MaxValue);
                options.OutputLength = (ushort)ReadInt(root, "eipOutputLength", options.OutputLength, 0, ushort.MaxValue);
                options.InputLength = (ushort)ReadInt(root, "eipInputLength", options.InputLength, 0, ushort.MaxValue);
                options.RequestedPacketIntervalMilliseconds = ReadInt(root, "eipRpiMilliseconds", options.RequestedPacketIntervalMilliseconds, 1, 10000);
                options.OutputRealTimeFormat = ReadString(root, "eipOutputRealTimeFormat", options.OutputRealTimeFormat);
                options.InputRealTimeFormat = ReadString(root, "eipInputRealTimeFormat", options.InputRealTimeFormat);
                options.InputConnectionType = ReadString(root, "eipInputConnectionType", options.InputConnectionType);
                options.InputDataOffset = ReadInt(root, "eipInputDataOffset", options.InputDataOffset, 0, 64);
                options.OutputDataOffset = ReadInt(root, "eipOutputDataOffset", options.OutputDataOffset, 0, 64);
                options.IoStaleTimeoutMilliseconds = ReadInt(
                    root,
                    "eipIoStaleTimeoutMilliseconds",
                    Math.Max(1000, options.RequestedPacketIntervalMilliseconds * 3),
                    100,
                    60000);
            }
            catch (JsonException)
            {
            }

            return options;
        }

        private static int ReadInt(JsonElement root, string name, int fallback, int min, int max)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return Math.Clamp(number, min, max);
            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return Math.Clamp(number, min, max);
            return fallback;
        }

        private static string ReadString(JsonElement root, string name, string fallback)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }
    }
}
