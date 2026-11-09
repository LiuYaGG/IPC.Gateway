using System;
using System.Text.Json;
using IPC.EdgeGateway;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Mqtt
{
    internal sealed class MqttSouthboundOptions
    {
        public string ClientId { get; private set; } = "IPC-Gateway-Southbound";
        public string SubscribeFilter { get; private set; } = "#";
        public string PayloadMode { get; private set; } = "Text";
        public bool UseTls { get; private set; }
        public bool AllowUntrustedCertificates { get; private set; }
        public int Qos { get; private set; }
        public int MaxValueAgeSeconds { get; private set; }

        public static MqttSouthboundOptions Parse(PlcConnectionOptions connection)
        {
            MqttSouthboundOptions options = new MqttSouthboundOptions();
            if (string.IsNullOrWhiteSpace(connection.DriverOptionsJson))
                return options;
            try
            {
                using JsonDocument document = JsonDocument.Parse(connection.DriverOptionsJson);
                JsonElement root = document.RootElement;
                options.ClientId = GetString(root, "mqttClientId", options.ClientId);
                options.SubscribeFilter = GetString(root, "mqttSubscribeFilter", options.SubscribeFilter);
                options.PayloadMode = GetString(root, "mqttPayloadMode", options.PayloadMode);
                options.UseTls = GetBoolean(root, "mqttUseTls", options.UseTls);
                options.AllowUntrustedCertificates = GetBoolean(root, "mqttAllowUntrustedCertificates", options.AllowUntrustedCertificates);
                options.Qos = GetInt32(root, "mqttQos", options.Qos, 0, 2);
                options.MaxValueAgeSeconds = GetInt32(root, "mqttMaxValueAgeSeconds", options.MaxValueAgeSeconds, 0, 86400);
            }
            catch (JsonException)
            {
            }
            return options;
        }

        public MqttGatewayOptions ToGatewayOptions(PlcConnectionOptions connection)
        {
            return new MqttGatewayOptions
            {
                Host = connection.Host,
                Port = connection.Port > 0 ? connection.Port : (UseTls ? 8883 : 1883),
                ClientId = ClientId,
                Username = connection.Username ?? string.Empty,
                Password = connection.Password ?? string.Empty,
                UseTls = UseTls,
                AllowUntrustedCertificates = AllowUntrustedCertificates,
                SubscribeTopic = SubscribeFilter,
                PublishQos = Qos,
                PublishAckTimeoutMilliseconds = Math.Max(100, connection.TimeoutMilliseconds)
            };
        }

        private static string GetString(JsonElement root, string name, string fallback)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback : fallback;

        private static bool GetBoolean(JsonElement root, string name, bool fallback)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean() : fallback;

        private static int GetInt32(JsonElement root, string name, int fallback, int min, int max)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
                return fallback;
            return Math.Clamp(result, min, max);
        }
    }
}
