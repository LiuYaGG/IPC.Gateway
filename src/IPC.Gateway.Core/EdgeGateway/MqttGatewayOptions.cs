/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttGatewayOptions
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

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class MqttGatewayOptions
    {
        public MqttGatewayOptions()
        {
            Enabled = false;
            GatewayId = "IPC-Gateway";
            GatewayName = "IPC Gateway";
            SiteName = string.Empty;
            CloudProtocolVersion = "ipc.gateway.v1";
            ConfigVersion = 1;
            PublishMode = "Classic";
            Host = "localhost";
            Port = 1883;
            ClientId = "IPC-Gateway";
            Username = string.Empty;
            Password = string.Empty;
            UseTls = false;
            AllowUntrustedCertificates = false;
            ClientCertificatePath = string.Empty;
            ClientCertificatePassword = string.Empty;
            ClientCertificateThumbprint = string.Empty;
            ServerCertificateThumbprint = string.Empty;
            CaCertificatePath = string.Empty;
            SubscribeTopic = "ipc/write/#";
            PublishEnabled = true;
            PublishSelectedTagsOnly = false;
            PublishChangedOnly = true;
            PublishUnchangedHeartbeatSeconds = 0;
            PublishTopicTemplate = "ipc/data/{channel}/{device}/{group}/{tag}";
            PublishQos = 0;
            HeartbeatEnabled = true;
            HeartbeatIntervalSeconds = 60;
            HeartbeatTopic = "gateway/{gatewayId}/heartbeat";
            HeartbeatQos = 0;
            StatusTopic = "gateway/{gatewayId}/status";
            CommandReplyTopicTemplate = "gateway/{gatewayId}/reply/{requestId}";
            OutboxDirectory = "Data\\MqttOutbox";
            PublishAckTimeoutMilliseconds = 5000;
            OutboxMaxMessages = 10000;
            OutboxMaxMegabytes = 100;
            OutboxRetentionHours = 168;
            OutboxQuarantineRetentionHours = 720;
            PublishFlushBatchSize = 100;
            PublishRetryMinSeconds = 1;
            PublishRetryMaxSeconds = 60;
            ReconnectSeconds = 5;
            KeepAliveSeconds = 30;
            SparkplugNamespace = "spBv1.0";
            SparkplugGroupId = "IPC-Gateway";
            SparkplugEdgeNodeId = "EdgeNode";
            SparkplugDeviceIdSource = "DeviceId";
            SparkplugMetricNameTemplate = "{channel}/{group}/{tag}";
            SparkplugPublishNodeBirth = true;
            SparkplugPublishDeviceBirth = true;
            SparkplugPublishDeviceDeath = true;
            SparkplugIncludeProperties = true;
            SparkplugUseAliases = false;
            SparkplugDeathQos = 0;
            SparkplugBirthQos = 0;
            SparkplugEnableCommands = true;
            SparkplugPrimaryHostId = string.Empty;
        }

        public bool Enabled { get; set; }
        public string GatewayId { get; set; }
        public string GatewayName { get; set; }
        public string SiteName { get; set; }
        public string CloudProtocolVersion { get; set; }
        public int ConfigVersion { get; set; }
        public string PublishMode { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseTls { get; set; }
        public bool AllowUntrustedCertificates { get; set; }
        public string ClientCertificatePath { get; set; }
        public string ClientCertificatePassword { get; set; }
        public string ClientCertificateThumbprint { get; set; }
        public string ServerCertificateThumbprint { get; set; }
        public string CaCertificatePath { get; set; }
        public string SubscribeTopic { get; set; }
        public bool PublishEnabled { get; set; }
        public bool PublishSelectedTagsOnly { get; set; }
        public bool PublishChangedOnly { get; set; }
        public int PublishUnchangedHeartbeatSeconds { get; set; }
        public string PublishTopicTemplate { get; set; }
        public int PublishQos { get; set; }
        public bool HeartbeatEnabled { get; set; }
        public int HeartbeatIntervalSeconds { get; set; }
        public string HeartbeatTopic { get; set; }
        public int HeartbeatQos { get; set; }
        public string StatusTopic { get; set; }
        public string CommandReplyTopicTemplate { get; set; }
        public string OutboxDirectory { get; set; }
        public int PublishAckTimeoutMilliseconds { get; set; }
        public int OutboxMaxMessages { get; set; }
        public int OutboxMaxMegabytes { get; set; }
        public int OutboxRetentionHours { get; set; }
        public int OutboxQuarantineRetentionHours { get; set; }
        public int PublishFlushBatchSize { get; set; }
        public int PublishRetryMinSeconds { get; set; }
        public int PublishRetryMaxSeconds { get; set; }
        public int ReconnectSeconds { get; set; }
        public int KeepAliveSeconds { get; set; }
        public string SparkplugNamespace { get; set; }
        public string SparkplugGroupId { get; set; }
        public string SparkplugEdgeNodeId { get; set; }
        public string SparkplugDeviceIdSource { get; set; }
        public string SparkplugMetricNameTemplate { get; set; }
        public bool SparkplugPublishNodeBirth { get; set; }
        public bool SparkplugPublishDeviceBirth { get; set; }
        public bool SparkplugPublishDeviceDeath { get; set; }
        public bool SparkplugIncludeProperties { get; set; }
        public bool SparkplugUseAliases { get; set; }
        public int SparkplugDeathQos { get; set; }
        public int SparkplugBirthQos { get; set; }
        public bool SparkplugEnableCommands { get; set; }
        public string SparkplugPrimaryHostId { get; set; }

        public string BrokerAddress
        {
            get
            {
                return (string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim()) + ":" +
                       ClampPort(Port).ToString(CultureInfo.InvariantCulture);
            }
        }

        public MqttGatewayOptions Clone()
        {
            return new MqttGatewayOptions
            {
                Enabled = Enabled,
                GatewayId = GatewayId,
                GatewayName = GatewayName,
                SiteName = SiteName,
                CloudProtocolVersion = CloudProtocolVersion,
                ConfigVersion = ConfigVersion,
                PublishMode = PublishMode,
                Host = Host,
                Port = Port,
                ClientId = ClientId,
                Username = Username,
                Password = Password,
                UseTls = UseTls,
                AllowUntrustedCertificates = AllowUntrustedCertificates,
                ClientCertificatePath = ClientCertificatePath,
                ClientCertificatePassword = ClientCertificatePassword,
                ClientCertificateThumbprint = ClientCertificateThumbprint,
                ServerCertificateThumbprint = ServerCertificateThumbprint,
                CaCertificatePath = CaCertificatePath,
                SubscribeTopic = SubscribeTopic,
                PublishEnabled = PublishEnabled,
                PublishSelectedTagsOnly = PublishSelectedTagsOnly,
                PublishChangedOnly = PublishChangedOnly,
                PublishUnchangedHeartbeatSeconds = PublishUnchangedHeartbeatSeconds,
                PublishTopicTemplate = PublishTopicTemplate,
                PublishQos = PublishQos,
                HeartbeatEnabled = HeartbeatEnabled,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                HeartbeatTopic = HeartbeatTopic,
                HeartbeatQos = HeartbeatQos,
                StatusTopic = StatusTopic,
                CommandReplyTopicTemplate = CommandReplyTopicTemplate,
                OutboxDirectory = OutboxDirectory,
                PublishAckTimeoutMilliseconds = PublishAckTimeoutMilliseconds,
                OutboxMaxMessages = OutboxMaxMessages,
                OutboxMaxMegabytes = OutboxMaxMegabytes,
                OutboxRetentionHours = OutboxRetentionHours,
                OutboxQuarantineRetentionHours = OutboxQuarantineRetentionHours,
                PublishFlushBatchSize = PublishFlushBatchSize,
                PublishRetryMinSeconds = PublishRetryMinSeconds,
                PublishRetryMaxSeconds = PublishRetryMaxSeconds,
                ReconnectSeconds = ReconnectSeconds,
                KeepAliveSeconds = KeepAliveSeconds,
                SparkplugNamespace = SparkplugNamespace,
                SparkplugGroupId = SparkplugGroupId,
                SparkplugEdgeNodeId = SparkplugEdgeNodeId,
                SparkplugDeviceIdSource = SparkplugDeviceIdSource,
                SparkplugMetricNameTemplate = SparkplugMetricNameTemplate,
                SparkplugPublishNodeBirth = SparkplugPublishNodeBirth,
                SparkplugPublishDeviceBirth = SparkplugPublishDeviceBirth,
                SparkplugPublishDeviceDeath = SparkplugPublishDeviceDeath,
                SparkplugIncludeProperties = SparkplugIncludeProperties,
                SparkplugUseAliases = SparkplugUseAliases,
                SparkplugDeathQos = SparkplugDeathQos,
                SparkplugBirthQos = SparkplugBirthQos,
                SparkplugEnableCommands = SparkplugEnableCommands,
                SparkplugPrimaryHostId = SparkplugPrimaryHostId
            };
        }

        public static int ClampPort(int port)
        {
            if (port < 1)
                return 1883;
            if (port > 65535)
                return 65535;
            return port;
        }

        public static int ClampReconnectSeconds(int seconds)
        {
            if (seconds < 1)
                return 5;
            if (seconds > 3600)
                return 3600;
            return seconds;
        }

        public static int ClampKeepAliveSeconds(int seconds)
        {
            if (seconds < 5)
                return 30;
            if (seconds > 3600)
                return 3600;
            return seconds;
        }

        public static int ClampQos(int qos)
        {
            if (qos < 0)
                return 0;
            if (qos > 2)
                return 2;
            return qos;
        }

        public static int ClampAckTimeoutMilliseconds(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 1000)
                return 5000;
            if (timeoutMilliseconds > 60000)
                return 60000;
            return timeoutMilliseconds;
        }

        public static int ClampOutboxMaxMessages(int value)
        {
            if (value < 100)
                return 100;
            if (value > 1000000)
                return 1000000;
            return value;
        }

        public static int ClampOutboxMaxMegabytes(int value)
        {
            if (value < 1)
                return 1;
            if (value > 102400)
                return 102400;
            return value;
        }

        public static int ClampOutboxRetentionHours(int value)
        {
            if (value < 1)
                return 1;
            if (value > 87600)
                return 87600;
            return value;
        }

        public static int ClampOutboxQuarantineRetentionHours(int value)
        {
            if (value < 1)
                return 1;
            if (value > 87600)
                return 87600;
            return value;
        }

        public static int ClampPublishFlushBatchSize(int value)
        {
            if (value < 1)
                return 1;
            if (value > 10000)
                return 10000;
            return value;
        }

        public static int ClampRetrySeconds(int value)
        {
            if (value < 1)
                return 1;
            if (value > 3600)
                return 3600;
            return value;
        }

        public static int ClampPublishUnchangedHeartbeatSeconds(int value)
        {
            if (value < 0)
                return 0;
            if (value > 86400)
                return 86400;
            return value;
        }

        public static int ClampHeartbeatIntervalSeconds(int value)
        {
            if (value < 5)
                return 5;
            if (value > 86400)
                return 86400;
            return value;
        }

        public static int ClampConfigVersion(int value)
        {
            return value < 1 ? 1 : value;
        }

        public static string NormalizePublishMode(string value)
        {
            return string.Equals(value, "SparkplugB", StringComparison.OrdinalIgnoreCase)
                ? "SparkplugB"
                : "Classic";
        }

        public static string NormalizeText(string value, string fallback)
        {
            string text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
    }
}
