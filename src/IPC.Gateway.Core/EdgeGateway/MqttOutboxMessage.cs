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
