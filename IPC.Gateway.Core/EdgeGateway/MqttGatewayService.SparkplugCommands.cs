using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using IPC.Gateway.Mqtt.Sparkplug;
using IPC.Runtime.Api;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class MqttGatewayService
    {
        private bool? _sparkplugPrimaryHostOnline;

        private IList<string> BuildSubscribeTopics()
        {
            List<string> topics = new List<string>
            {
                string.IsNullOrWhiteSpace(_options.SubscribeTopic) ? "ipc/write/#" : _options.SubscribeTopic.Trim()
            };
            if (!IsSparkplugMode())
                return topics;

            SparkplugTopicBuilder builder = CreateSparkplugTopicBuilder();
            if (_options.SparkplugEnableCommands)
            {
                topics.Add(builder.NodeCommand());
                topics.Add(builder.DeviceCommandFilter());
            }
            if (!string.IsNullOrWhiteSpace(_options.SparkplugPrimaryHostId))
                topics.Add(builder.PrimaryHostState(_options.SparkplugPrimaryHostId));
            return topics;
        }

        private bool TryHandleSparkplugMessage(MqttMessageEventArgs message)
        {
            if (!IsSparkplugMode() || message == null)
                return false;

            SparkplugTopicBuilder builder = CreateSparkplugTopicBuilder();
            if (!string.IsNullOrWhiteSpace(_options.SparkplugPrimaryHostId) &&
                string.Equals(message.Topic, builder.PrimaryHostState(_options.SparkplugPrimaryHostId), StringComparison.Ordinal))
            {
                HandlePrimaryHostState(message.Payload);
                return true;
            }

            if (!_options.SparkplugEnableCommands)
                return false;
            if (string.Equals(message.Topic, builder.NodeCommand(), StringComparison.Ordinal))
            {
                HandleNodeCommand(message.PayloadBytes);
                return true;
            }

            string devicePrefix = builder.DeviceCommand(string.Empty).TrimEnd('/') + "/";
            if (!message.Topic.StartsWith(devicePrefix, StringComparison.Ordinal))
                return false;

            string deviceId = message.Topic.Substring(devicePrefix.Length).Trim('/');
            HandleDeviceCommand(deviceId, message.PayloadBytes);
            return true;
        }

        private void HandleNodeCommand(byte[] payloadBytes)
        {
            try
            {
                SparkplugPayload payload = SparkplugPayloadDecoder.Decode(payloadBytes ?? Array.Empty<byte>());
                SparkplugMetric? rebirth = payload.Metrics.FirstOrDefault(metric =>
                    string.Equals(metric.Name, "Node Control/Rebirth", StringComparison.OrdinalIgnoreCase));
                if (rebirth == null || !ToBoolean(rebirth.Value))
                    return;

                lock (_syncRoot)
                    _sparkplugPayloadSequence = 255;
                TryQueueSparkplugBirth();
            }
            catch (Exception ex)
            {
                MarkWriteFailed("Sparkplug NCMD 解析失败：" + ex.Message);
            }
        }

        private void HandleDeviceCommand(string deviceId, byte[] payloadBytes)
        {
            try
            {
                SparkplugPayload payload = SparkplugPayloadDecoder.Decode(payloadBytes ?? Array.Empty<byte>());
                Dictionary<string, List<TagValueSnapshot>> devices = BuildSparkplugDeviceSnapshotMap();
                if (!devices.TryGetValue(deviceId, out List<TagValueSnapshot>? snapshots))
                {
                    MarkWriteFailed("Sparkplug DCMD 设备不存在：" + deviceId);
                    return;
                }

                foreach (SparkplugMetric metric in payload.Metrics)
                {
                    TagValueSnapshot? snapshot = snapshots.FirstOrDefault(item => MatchesCommandMetric(item, metric));
                    if (snapshot == null)
                    {
                        MarkWriteFailed("Sparkplug DCMD 指标不存在：" + (metric.Name ?? metric.Alias?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
                        continue;
                    }

                    string valueText = FormatCommandValue(metric.Value);
                    WriteTagResponse response = _runtime.WriteTag(new WriteTagRequest
                    {
                        ChannelId = snapshot.ChannelId,
                        ChannelName = snapshot.ChannelName,
                        DeviceId = snapshot.DeviceId,
                        DeviceName = snapshot.DeviceName,
                        GroupId = snapshot.GroupId,
                        GroupName = snapshot.GroupName,
                        TagId = snapshot.TagId,
                        TagName = snapshot.TagName,
                        DataType = snapshot.DataType,
                        Value = valueText,
                        ValueText = valueText
                    });
                    if (response?.Success == true)
                    {
                        UpdateStatus(status =>
                        {
                            status.SuccessfulWrites++;
                            status.LastWriteResult = "Sparkplug DCMD 写入成功：" + snapshot.TagName;
                            status.LastError = string.Empty;
                        });
                    }
                    else
                    {
                        MarkWriteFailed(response?.ErrorMessage ?? "Sparkplug DCMD 写入失败。");
                    }
                }
            }
            catch (Exception ex)
            {
                MarkWriteFailed("Sparkplug DCMD 解析失败：" + ex.Message);
            }
        }

        private bool MatchesCommandMetric(TagValueSnapshot snapshot, SparkplugMetric metric)
        {
            if (metric.Alias.HasValue && metric.Alias.Value == BuildSparkplugMetricAlias(snapshot))
                return true;
            return !string.IsNullOrWhiteSpace(metric.Name) &&
                   string.Equals(metric.Name, BuildSparkplugMetricName(snapshot), StringComparison.OrdinalIgnoreCase);
        }

        private void HandlePrimaryHostState(string payload)
        {
            bool? online = ParsePrimaryHostOnline(payload);
            if (!online.HasValue)
                return;

            bool shouldRebirth = online.Value && _sparkplugPrimaryHostOnline != true;
            _sparkplugPrimaryHostOnline = online;
            if (shouldRebirth)
            {
                lock (_syncRoot)
                    _sparkplugPayloadSequence = 255;
                TryQueueSparkplugBirth();
            }
        }

        private static bool? ParsePrimaryHostOnline(string payload)
        {
            string text = (payload ?? string.Empty).Trim();
            if (text.Equals("ONLINE", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("online", out JsonElement online) &&
                    (online.ValueKind == JsonValueKind.True || online.ValueKind == JsonValueKind.False))
                    return online.GetBoolean();
            }
            catch (JsonException)
            {
            }
            return null;
        }

        private static bool ToBoolean(object? value)
        {
            if (value is bool boolean)
                return boolean;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }

        private static string FormatCommandValue(object? value)
        {
            if (value is Array array)
            {
                List<string> values = new List<string>();
                foreach (object? item in array)
                    values.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                return string.Join(",", values);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
