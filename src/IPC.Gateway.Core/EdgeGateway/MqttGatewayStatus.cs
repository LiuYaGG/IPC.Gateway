/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttGatewayStatus
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
using IPC.Gateway.Core.Resilience;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class MqttGatewayStatus
    {
        public MqttGatewayStatus()
        {
            Broker = string.Empty;
            GatewayId = string.Empty;
            GatewayName = string.Empty;
            SiteName = string.Empty;
            CloudProtocolVersion = string.Empty;
            PublishMode = string.Empty;
            SubscribeTopic = string.Empty;
            PublishTopicTemplate = string.Empty;
            HeartbeatTopic = string.Empty;
            StatusTopic = string.Empty;
            CommandReplyTopicTemplate = string.Empty;
            SparkplugNamespace = string.Empty;
            SparkplugGroupId = string.Empty;
            SparkplugEdgeNodeId = string.Empty;
            SparkplugNodeBirthTopic = string.Empty;
            SparkplugNodeDeathTopic = string.Empty;
            OutboxDirectory = string.Empty;
            OutboxQuarantineDirectory = string.Empty;
            LastError = string.Empty;
            LastMessage = string.Empty;
            LastWriteResult = string.Empty;
            LastPublishResult = string.Empty;
            CircuitBreaker = new CircuitBreakerStatus { Name = "MQTT", Enabled = true };
            LastConnectedTime = DateTime.MinValue;
            LastMessageTime = DateTime.MinValue;
            LastPublishTime = DateTime.MinValue;
            LastPublishFailureTime = DateTime.MinValue;
            LastSparkplugBirthTime = DateTime.MinValue;
            LastSparkplugDeathTime = DateTime.MinValue;
            NextPublishRetryTime = DateTime.MinValue;
            OutboxOldestPendingTime = DateTime.MinValue;
            OutboxNewestPendingTime = DateTime.MinValue;
            OutboxOldestQuarantineTime = DateTime.MinValue;
            OutboxNewestQuarantineTime = DateTime.MinValue;
        }

        public bool Enabled { get; set; }
        public string GatewayId { get; set; }
        public string GatewayName { get; set; }
        public string SiteName { get; set; }
        public string CloudProtocolVersion { get; set; }
        public int ConfigVersion { get; set; }
        public string PublishMode { get; set; }
        public bool IsRunning { get; set; }
        public bool IsConnected { get; set; }
        public string Broker { get; set; }
        public string SubscribeTopic { get; set; }
        public bool PublishEnabled { get; set; }
        public string PublishTopicTemplate { get; set; }
        public int PublishQos { get; set; }
        public string HeartbeatTopic { get; set; }
        public string StatusTopic { get; set; }
        public string CommandReplyTopicTemplate { get; set; }
        public bool SparkplugEnabled { get; set; }
        public string SparkplugNamespace { get; set; }
        public string SparkplugGroupId { get; set; }
        public string SparkplugEdgeNodeId { get; set; }
        public string SparkplugNodeBirthTopic { get; set; }
        public string SparkplugNodeDeathTopic { get; set; }
        public string OutboxDirectory { get; set; }
        public string OutboxQuarantineDirectory { get; set; }
        public string LastError { get; set; }
        public string LastMessage { get; set; }
        public string LastWriteResult { get; set; }
        public string LastPublishResult { get; set; }
        public CircuitBreakerStatus CircuitBreaker { get; set; }
        public DateTime LastConnectedTime { get; set; }
        public DateTime LastMessageTime { get; set; }
        public DateTime LastPublishTime { get; set; }
        public DateTime LastPublishFailureTime { get; set; }
        public DateTime LastSparkplugBirthTime { get; set; }
        public DateTime LastSparkplugDeathTime { get; set; }
        public DateTime NextPublishRetryTime { get; set; }
        public int ReconnectCount { get; set; }
        public int ReceivedCount { get; set; }
        public int SuccessfulWrites { get; set; }
        public int FailedWrites { get; set; }
        public int PublishedCount { get; set; }
        public int FailedPublishes { get; set; }
        public int SparkplugBirthCount { get; set; }
        public int SparkplugDeathCount { get; set; }
        public int SparkplugDataCount { get; set; }
        public int OutboxPendingCount { get; set; }
        public int OutboxEnqueuedCount { get; set; }
        public long OutboxBytes { get; set; }
        public int OutboxExpiredDeletedCount { get; set; }
        public int OutboxOverflowDeletedCount { get; set; }
        public int OutboxInvalidMessageCount { get; set; }
        public int OutboxQuarantinedMessageCount { get; set; }
        public int OutboxQuarantineCount { get; set; }
        public long OutboxQuarantineBytes { get; set; }
        public int OutboxQuarantineExpiredDeletedCount { get; set; }
        public DateTime OutboxOldestPendingTime { get; set; }
        public DateTime OutboxNewestPendingTime { get; set; }
        public DateTime OutboxOldestQuarantineTime { get; set; }
        public DateTime OutboxNewestQuarantineTime { get; set; }
        public long OutboxOldestPendingAgeSeconds { get; set; }
        public int PublishRetryBackoffSeconds { get; set; }
        public int PublishConsecutiveFailureCount { get; set; }

        public MqttGatewayStatus Clone()
        {
            return (MqttGatewayStatus)MemberwiseClone();
        }
    }
}
