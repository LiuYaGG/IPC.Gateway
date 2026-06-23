/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：MqttGatewayOptionsStore
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.IO;
using System.Text;
using System.Text.Json;
using IPC.EdgeGateway;

namespace IPC.Gateway.Core.Gateway
{
    
    
    
    
    
    
    
    
    
    public sealed class MqttGatewayOptionsStore
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public MqttGatewayOptionsStore()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public MqttGatewayOptions LoadOrDefault(string path, MqttGatewayOptions defaultOptions)
        {
            string resolvedPath = ResolvePath(path);
            if (!File.Exists(resolvedPath))
            {
                MqttGatewayOptions options = defaultOptions == null ? new MqttGatewayOptions() : defaultOptions.Clone();
                Save(resolvedPath, options);
                return options;
            }

            string json = File.ReadAllText(resolvedPath, Encoding.UTF8);
            MqttGatewayOptions? loaded = JsonSerializer.Deserialize<MqttGatewayOptions>(json, _jsonOptions);
            return loaded == null ? new MqttGatewayOptions() : Normalize(loaded);
        }

        public void Save(string path, MqttGatewayOptions options)
        {
            string resolvedPath = ResolvePath(path);
            string? directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(Normalize(options == null ? new MqttGatewayOptions() : options.Clone()), _jsonOptions);
            File.WriteAllText(resolvedPath, json, new UTF8Encoding(false));
        }

        private static MqttGatewayOptions Normalize(MqttGatewayOptions options)
        {
            options.GatewayId = string.IsNullOrWhiteSpace(options.GatewayId) ? "IPC-Gateway" : options.GatewayId.Trim();
            options.GatewayName = string.IsNullOrWhiteSpace(options.GatewayName) ? "IPC Gateway" : options.GatewayName.Trim();
            options.SiteName = options.SiteName ?? string.Empty;
            options.CloudProtocolVersion = string.IsNullOrWhiteSpace(options.CloudProtocolVersion) ? "ipc.gateway.v1" : options.CloudProtocolVersion.Trim();
            options.Host = string.IsNullOrWhiteSpace(options.Host) ? "localhost" : options.Host.Trim();
            options.ClientId = string.IsNullOrWhiteSpace(options.ClientId) ? options.GatewayId : options.ClientId.Trim();
            options.Username = options.Username ?? string.Empty;
            options.Password = options.Password ?? string.Empty;
            options.ClientCertificatePath = options.ClientCertificatePath ?? string.Empty;
            options.ClientCertificatePassword = options.ClientCertificatePassword ?? string.Empty;
            options.ClientCertificateThumbprint = options.ClientCertificateThumbprint ?? string.Empty;
            options.ServerCertificateThumbprint = options.ServerCertificateThumbprint ?? string.Empty;
            options.CaCertificatePath = options.CaCertificatePath ?? string.Empty;
            options.SubscribeTopic = string.IsNullOrWhiteSpace(options.SubscribeTopic) ? "ipc/write/#" : options.SubscribeTopic.Trim();
            options.PublishTopicTemplate = string.IsNullOrWhiteSpace(options.PublishTopicTemplate) ? "ipc/data/{device}/{group}/{tag}" : options.PublishTopicTemplate.Trim();
            options.HeartbeatTopic = string.IsNullOrWhiteSpace(options.HeartbeatTopic) ? "gateway/{gatewayId}/heartbeat" : options.HeartbeatTopic.Trim();
            options.StatusTopic = string.IsNullOrWhiteSpace(options.StatusTopic) ? "gateway/{gatewayId}/status" : options.StatusTopic.Trim();
            options.CommandReplyTopicTemplate = string.IsNullOrWhiteSpace(options.CommandReplyTopicTemplate) ? "gateway/{gatewayId}/reply/{requestId}" : options.CommandReplyTopicTemplate.Trim();
            options.OutboxDirectory = string.IsNullOrWhiteSpace(options.OutboxDirectory) ? "Data\\MqttOutbox" : options.OutboxDirectory.Trim();
            options.Port = MqttGatewayOptions.ClampPort(options.Port);
            options.PublishQos = MqttGatewayOptions.ClampQos(options.PublishQos);
            options.HeartbeatQos = MqttGatewayOptions.ClampQos(options.HeartbeatQos);
            options.HeartbeatIntervalSeconds = MqttGatewayOptions.ClampHeartbeatIntervalSeconds(options.HeartbeatIntervalSeconds);
            options.PublishAckTimeoutMilliseconds = MqttGatewayOptions.ClampAckTimeoutMilliseconds(options.PublishAckTimeoutMilliseconds);
            options.PublishUnchangedHeartbeatSeconds = MqttGatewayOptions.ClampPublishUnchangedHeartbeatSeconds(options.PublishUnchangedHeartbeatSeconds);
            options.OutboxMaxMessages = MqttGatewayOptions.ClampOutboxMaxMessages(options.OutboxMaxMessages);
            options.OutboxMaxMegabytes = MqttGatewayOptions.ClampOutboxMaxMegabytes(options.OutboxMaxMegabytes);
            options.OutboxRetentionHours = MqttGatewayOptions.ClampOutboxRetentionHours(options.OutboxRetentionHours);
            options.OutboxQuarantineRetentionHours = MqttGatewayOptions.ClampOutboxQuarantineRetentionHours(options.OutboxQuarantineRetentionHours);
            options.PublishFlushBatchSize = MqttGatewayOptions.ClampPublishFlushBatchSize(options.PublishFlushBatchSize);
            options.PublishRetryMinSeconds = MqttGatewayOptions.ClampRetrySeconds(options.PublishRetryMinSeconds);
            options.PublishRetryMaxSeconds = MqttGatewayOptions.ClampRetrySeconds(options.PublishRetryMaxSeconds);
            options.ReconnectSeconds = MqttGatewayOptions.ClampReconnectSeconds(options.ReconnectSeconds);
            options.KeepAliveSeconds = MqttGatewayOptions.ClampKeepAliveSeconds(options.KeepAliveSeconds);
            options.ConfigVersion = MqttGatewayOptions.ClampConfigVersion(options.ConfigVersion);
            options.PublishMode = MqttGatewayOptions.NormalizePublishMode(options.PublishMode);
            options.SparkplugNamespace = MqttGatewayOptions.NormalizeText(options.SparkplugNamespace, "spBv1.0");
            options.SparkplugGroupId = MqttGatewayOptions.NormalizeText(options.SparkplugGroupId, options.GatewayId);
            options.SparkplugEdgeNodeId = MqttGatewayOptions.NormalizeText(options.SparkplugEdgeNodeId, options.ClientId);
            options.SparkplugDeviceIdSource = MqttGatewayOptions.NormalizeText(options.SparkplugDeviceIdSource, "DeviceName");
            options.SparkplugMetricNameTemplate = MqttGatewayOptions.NormalizeText(options.SparkplugMetricNameTemplate, "{group}/{tag}");
            options.SparkplugDeathQos = MqttGatewayOptions.ClampQos(options.SparkplugDeathQos);
            options.SparkplugBirthQos = MqttGatewayOptions.ClampQos(options.SparkplugBirthQos);
            return options;
        }

        private static string ResolvePath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? "Data\\gateway-mqtt.json" : path.Trim();
            if (!Path.IsPathRooted(value))
                value = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            return value;
        }
    }
}
