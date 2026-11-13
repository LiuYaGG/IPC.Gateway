/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttOutboxMessage
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    internal sealed class MqttOutboxMessage
    {
        public MqttOutboxMessage()
        {
            Id = 0;
            Topic = string.Empty;
            Payload = string.Empty;
            PayloadFormat = "Text";
            PayloadBytes = Array.Empty<byte>();
            CreatedAt = DateTime.Now;
            Qos = 0;
        }

        public long Id { get; set; }
        public string Topic { get; set; }
        public string Payload { get; set; }
        public string PayloadFormat { get; set; }
        public byte[] PayloadBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Qos { get; set; }

        // Old outbox records have no origin field and the gateway status topic is configurable.
        // Recognize the built-in publisher's envelope before removing transport metrics and inventory.
        public string? CompactLegacyGatewayStatus()
        {
            if (PayloadFormat != "Text" ||
                !Payload.StartsWith("{\"messageType\":\"gatewayStatus\",\"protocolVersion\":", StringComparison.Ordinal) ||
                !Payload.Contains("\"mqtt\":{", StringComparison.Ordinal))
                return null;

            try
            {
                using var document = JsonDocument.Parse(Payload);
                var root = document.RootElement;
                if (!IsLegacyGatewayStatus(root))
                    return null;

                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = new Utf8JsonWriter(buffer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    writer.WriteStartObject();
                    foreach (var property in root.EnumerateObject())
                        if (!property.NameEquals("mqtt") && !property.NameEquals("channels"))
                            property.WriteTo(writer);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }

        private static bool IsLegacyGatewayStatus(JsonElement root)
        {
            if (!HasProperty(root, "protocolVersion", JsonValueKind.String) ||
                !HasProperty(root, "gatewayId", JsonValueKind.String) ||
                !HasProperty(root, "gatewayName", JsonValueKind.String) ||
                !HasProperty(root, "siteName", JsonValueKind.String) ||
                !HasProperty(root, "configVersion", JsonValueKind.Number) ||
                !HasProperty(root, "timestamp", JsonValueKind.String) ||
                !root.TryGetProperty("mqtt", out var mqtt) || mqtt.ValueKind != JsonValueKind.Object ||
                !HasProperty(mqtt, "broker", JsonValueKind.String) ||
                !HasProperty(mqtt, "outboxPending", JsonValueKind.Number) ||
                !mqtt.TryGetProperty("connected", out var connected) ||
                connected.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;

            // The first migration omitted channels. The subsequent publisher also added isRunning.
            // Each version emitted exactly this set of top-level properties.
            int propertyCount = root.GetPropertyCount();
            if (!root.TryGetProperty("channels", out var channels))
            {
                if (root.TryGetProperty("isRunning", out var isRunning))
                    return propertyCount == 9 && isRunning.ValueKind is JsonValueKind.True or JsonValueKind.False;
                return propertyCount == 8;
            }
            if (propertyCount != 9 || channels.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var channel in channels.EnumerateArray())
                if (channel.ValueKind != JsonValueKind.Object ||
                    !HasProperty(channel, "channelId", JsonValueKind.String) ||
                    !HasProperty(channel, "channelName", JsonValueKind.String) ||
                    !HasProperty(channel, "deviceCount", JsonValueKind.Number) ||
                    !HasProperty(channel, "devices", JsonValueKind.Array))
                    return false;
            return true;
        }

        private static bool HasProperty(JsonElement element, string name, JsonValueKind kind) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == kind;

        public string ToFileText()
        {
            string format = string.Equals(PayloadFormat, "Binary", StringComparison.OrdinalIgnoreCase) ? "Binary" : "Text";
            string payload = string.Equals(format, "Binary", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToBase64String(PayloadBytes ?? Array.Empty<byte>())
                : Encode(Payload);

            return "Id=" + Id.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Qos=" + MqttGatewayOptions.ClampQos(Qos).ToString(CultureInfo.InvariantCulture) + "\n" +
                   "CreatedAt=" + CreatedAt.ToString("o", CultureInfo.InvariantCulture) + "\n" +
                   "Topic=" + Encode(Topic) + "\n" +
                   "PayloadFormat=" + format + "\n" +
                   "Payload=" + payload + "\n";
        }

        public byte[] GetPayloadBytes()
        {
            if (string.Equals(PayloadFormat, "Binary", StringComparison.OrdinalIgnoreCase))
                return PayloadBytes ?? Array.Empty<byte>();

            return System.Text.Encoding.UTF8.GetBytes(Payload ?? string.Empty);
        }

        public string GetPayloadPreview()
        {
            if (string.Equals(PayloadFormat, "Binary", StringComparison.OrdinalIgnoreCase))
                return "base64:" + Convert.ToBase64String(PayloadBytes ?? Array.Empty<byte>());

            return Payload ?? string.Empty;
        }

        public static bool TryParse(string? text, [NotNullWhen(true)] out MqttOutboxMessage? message)
        {
            message = null;
            if (string.IsNullOrEmpty(text))
                return false;

            MqttOutboxMessage parsed = new MqttOutboxMessage();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);
                if (string.Equals(key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    long id;
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        parsed.Id = id;
                }
                else if (string.Equals(key, "Qos", StringComparison.OrdinalIgnoreCase))
                {
                    int qos;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out qos))
                        parsed.Qos = MqttGatewayOptions.ClampQos(qos);
                }
                else if (string.Equals(key, "CreatedAt", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime createdAt;
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out createdAt))
                        parsed.CreatedAt = createdAt;
                }
                else if (string.Equals(key, "Topic", StringComparison.OrdinalIgnoreCase))
                {
                    string? topic;
                    if (!TryDecode(value, out topic))
                        return false;
                    parsed.Topic = topic;
                }
                else if (string.Equals(key, "Payload", StringComparison.OrdinalIgnoreCase))
                {
                    string? payload;
                    if (string.Equals(parsed.PayloadFormat, "Binary", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            parsed.PayloadBytes = Convert.FromBase64String(value);
                            parsed.Payload = string.Empty;
                        }
                        catch (FormatException)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!TryDecode(value, out payload))
                            return false;
                        parsed.Payload = payload;
                        parsed.PayloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                    }
                }
                else if (string.Equals(key, "PayloadFormat", StringComparison.OrdinalIgnoreCase))
                {
                    parsed.PayloadFormat = string.Equals(value, "Binary", StringComparison.OrdinalIgnoreCase) ? "Binary" : "Text";
                }
            }

            if (parsed.Id <= 0 || string.IsNullOrWhiteSpace(parsed.Topic))
                return false;

            message = parsed;
            return true;
        }

        private static string Encode(string value)
        {
            if (value == null)
                value = string.Empty;

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(bytes);
        }

        private static bool TryDecode(string value, [NotNullWhen(true)] out string? decoded)
        {
            decoded = null;
            if (string.IsNullOrEmpty(value))
            {
                decoded = string.Empty;
                return true;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                decoded = System.Text.Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
