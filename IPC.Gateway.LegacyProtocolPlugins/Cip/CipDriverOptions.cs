using System;
using System.Text.Json;

namespace IPC.Plc.Communication.Cip
{
    internal sealed class CipDriverOptions
    {
        public string RouteMode { get; private set; } = "Slot";

        public string RoutePath { get; private set; } = string.Empty;

        public string ControllerProfile { get; private set; } = "Logix";

        public int MaxRequestBytes { get; private set; } = 400;

        public int MaxServicesPerPacket { get; private set; } = 16;

        public bool RouteModeSpecified { get; private set; }

        public static CipDriverOptions Parse(string json)
        {
            CipDriverOptions options = new CipDriverOptions();
            if (string.IsNullOrWhiteSpace(json))
                return options;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                options.RouteModeSpecified = root.TryGetProperty("cipRouteMode", out _);
                options.RouteMode = GetString(root, "cipRouteMode", options.RouteMode);
                options.RoutePath = GetString(root, "cipRoutePath", options.RoutePath);
                options.ControllerProfile = GetString(root, "controllerProfile", options.ControllerProfile);
                options.MaxRequestBytes = GetInt32(root, "cipMaxRequestBytes", options.MaxRequestBytes, 64, 4000);
                options.MaxServicesPerPacket = GetInt32(root, "cipMaxServicesPerPacket", options.MaxServicesPerPacket, 1, 64);

                if (options.ControllerProfile.Equals("Micro800", StringComparison.OrdinalIgnoreCase))
                {
                    if (!root.TryGetProperty("cipRouteMode", out _))
                        options.RouteMode = "Direct";
                    if (!root.TryGetProperty("cipMaxRequestBytes", out _))
                        options.MaxRequestBytes = 240;
                    if (!root.TryGetProperty("cipMaxServicesPerPacket", out _))
                        options.MaxServicesPerPacket = 1;
                }
                else if (options.ControllerProfile.Equals("Generic", StringComparison.OrdinalIgnoreCase) &&
                         !root.TryGetProperty("cipRouteMode", out _))
                {
                    options.RouteMode = "Direct";
                }
            }
            catch (JsonException)
            {
                // Invalid optional driver JSON must not break legacy Slot-based configurations.
            }

            return options;
        }

        private static string GetString(JsonElement root, string name, string defaultValue)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value))
                return defaultValue;
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? defaultValue
                : defaultValue;
        }

        private static int GetInt32(JsonElement root, string name, int defaultValue, int min, int max)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
                return defaultValue;
            return Math.Max(min, Math.Min(max, result));
        }
    }
}
