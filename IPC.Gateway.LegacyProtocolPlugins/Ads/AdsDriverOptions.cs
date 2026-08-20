using System;
using System.Net;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Ads
{
    internal sealed class AdsDriverOptions
    {
        public string AmsNetId { get; private set; } = string.Empty;
        public int AdsPort { get; private set; } = 851;
        public int StringLength { get; private set; } = 80;
        public int MaxBatchItems { get; private set; } = 100;

        public static AdsDriverOptions Parse(PlcConnectionOptions connection)
        {
            AdsDriverOptions options = new AdsDriverOptions();
            if (!string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                    JsonElement root = document.RootElement;
                    options.AmsNetId = GetString(root, "amsNetId", options.AmsNetId);
                    options.AdsPort = GetInt32(root, "adsPort", options.AdsPort, 1, 65535);
                    options.StringLength = GetInt32(root, "adsStringLength", options.StringLength, 1, 4096);
                    options.MaxBatchItems = GetInt32(root, "adsMaxBatchItems", options.MaxBatchItems, 1, 500);
                }
                catch (JsonException)
                {
                }
            }

            if (string.IsNullOrWhiteSpace(options.AmsNetId) &&
                IPAddress.TryParse(connection.Host, out IPAddress address) &&
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                options.AmsNetId = connection.Host.Trim() + ".1.1";
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
