using System;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Snmp
{
    internal sealed class SnmpDriverOptions
    {
        public string Version { get; private set; } = "V2c";
        public string Community { get; private set; } = "public";
        public string UserName { get; private set; } = string.Empty;
        public string AuthenticationProtocol { get; private set; } = "None";
        public string AuthenticationPassword { get; private set; } = string.Empty;
        public string PrivacyProtocol { get; private set; } = "None";
        public string PrivacyPassword { get; private set; } = string.Empty;
        public string ContextName { get; private set; } = string.Empty;
        public int MaxOidsPerRequest { get; private set; } = 40;

        public static SnmpDriverOptions Parse(PlcConnectionOptions connection)
        {
            SnmpDriverOptions options = new SnmpDriverOptions();
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;
            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.Version = GetString(root, "snmpVersion", options.Version);
                options.Community = GetString(root, "snmpCommunity", options.Community);
                options.UserName = GetString(root, "snmpUserName", options.UserName);
                options.AuthenticationProtocol = GetString(root, "snmpAuthProtocol", options.AuthenticationProtocol);
                options.AuthenticationPassword = GetString(root, "snmpAuthPassword", options.AuthenticationPassword);
                options.PrivacyProtocol = GetString(root, "snmpPrivacyProtocol", options.PrivacyProtocol);
                options.PrivacyPassword = GetString(root, "snmpPrivacyPassword", options.PrivacyPassword);
                options.ContextName = GetString(root, "snmpContextName", options.ContextName);
                options.MaxOidsPerRequest = GetInt32(root, "snmpMaxOidsPerRequest", options.MaxOidsPerRequest, 1, 100);
            }
            catch (JsonException)
            {
            }
            return options;
        }

        private static string GetString(JsonElement root, string name, string fallback)
        {
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
                return fallback;
            return Math.Clamp(result, min, max);
        }
    }
}
