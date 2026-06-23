/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：MqttOutboxStoreTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Tests;

public sealed class MqttOutboxStoreTests
{
    [Fact]
    public void EnqueueBinaryPayload_RoundTripsPayloadBytes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            byte[] payload = { 0x08, 0x01, 0x12, 0x02, 0x08, 0x02 };
            MqttOutboxStore store = new MqttOutboxStore(directory);

            store.Enqueue("spBv1.0/group/DDATA/node/device", payload, 2);

            MqttOutboxEntry entry = Assert.Single(store.ListPending(10));
            Assert.Equal("Binary", entry.Message.PayloadFormat);
            Assert.Equal(payload, entry.Message.GetPayloadBytes());
            Assert.StartsWith("base64:", entry.Message.GetPayloadPreview(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, entry.Message.Qos);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void GetStats_ReportsPendingTimeRangeAndInvalidFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            DateTime oldest = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
            DateTime newest = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Local);

            WriteMessage(directory, "00000000000000001.msg", 1, "ipc/a", oldest);
            WriteMessage(directory, "00000000000000002.msg", 2, "ipc/b", newest);
            File.WriteAllText(Path.Combine(directory, "00000000000000003.msg"), "not a valid outbox message");

            MqttOutboxStore store = new MqttOutboxStore(directory);
            MqttOutboxStats stats = store.GetStats();

            Assert.Equal(3, stats.MessageCount);
            Assert.Equal(1, stats.InvalidMessageCount);
            Assert.Equal(oldest, stats.OldestCreatedAt);
            Assert.Equal(newest, stats.NewestCreatedAt);
            Assert.True(stats.TotalBytes > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Cleanup_QuarantinesInvalidFilesAndKeepsValidMessages()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            WriteMessage(directory, "00000000000000001.msg", 1, "ipc/a", DateTime.Now);
            File.WriteAllText(Path.Combine(directory, "00000000000000002.msg"), "bad message");

            MqttOutboxStore store = new MqttOutboxStore(directory);

            MqttOutboxCleanupResult result = store.Cleanup(100, 1024 * 1024, TimeSpan.FromDays(7));
            MqttOutboxStats stats = store.GetStats();
            MqttOutboxQuarantineStats quarantineStats = store.GetQuarantineStats();

            Assert.Equal(1, result.InvalidQuarantined);
            Assert.Equal(1, result.RemainingCount);
            Assert.Equal(1, result.RemainingQuarantineCount);
            Assert.True(result.RemainingQuarantineBytes > 0);
            Assert.Equal(1, stats.MessageCount);
            Assert.Equal(0, stats.InvalidMessageCount);
            Assert.Equal(1, quarantineStats.MessageCount);
            Assert.True(quarantineStats.TotalBytes > 0);
            Assert.True(File.Exists(Path.Combine(store.QuarantineDirectoryPath, "00000000000000002.msg")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Cleanup_DeletesExpiredQuarantineFilesAndKeepsRecentFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            MqttOutboxStore store = new MqttOutboxStore(directory);
            Directory.CreateDirectory(store.QuarantineDirectoryPath);
            string oldFile = Path.Combine(store.QuarantineDirectoryPath, "00000000000000001.msg");
            string recentFile = Path.Combine(store.QuarantineDirectoryPath, "00000000000000002.msg");
            File.WriteAllText(oldFile, "old bad message");
            File.WriteAllText(recentFile, "recent bad message");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow);

            MqttOutboxCleanupResult result = store.Cleanup(100, 1024 * 1024, TimeSpan.FromDays(7), TimeSpan.FromHours(1));
            MqttOutboxQuarantineStats quarantineStats = store.GetQuarantineStats();

            Assert.Equal(1, result.QuarantineExpiredDeleted);
            Assert.Equal(1, result.RemainingQuarantineCount);
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(recentFile));
            Assert.Equal(1, quarantineStats.MessageCount);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryParse_InvalidBase64PayloadReturnsFalse()
    {
        string text = "Id=1\nQos=0\nCreatedAt=2026-01-01T00:00:00.0000000+08:00\nTopic=not-base64\nPayload=e30=\n";

        bool parsed = MqttOutboxMessage.TryParse(text, out MqttOutboxMessage? message);

        Assert.False(parsed);
        Assert.Null(message);
    }

    [Fact]
    public void MqttGatewayOptionsStore_LoadOrDefaultNormalizesLoadedOptions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "mqtt.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
{
  "gatewayId": null,
  "gatewayName": "  ",
  "siteName": null,
  "cloudProtocolVersion": null,
  "host": null,
  "clientId": null,
  "username": null,
  "password": null,
  "subscribeTopic": null,
  "publishTopicTemplate": null,
  "heartbeatTopic": null,
  "statusTopic": null,
  "commandReplyTopicTemplate": null,
  "outboxDirectory": null,
  "port": -1,
  "publishQos": 5,
  "heartbeatQos": -3,
  "heartbeatIntervalSeconds": 1,
  "publishAckTimeoutMilliseconds": 10,
  "publishUnchangedHeartbeatSeconds": 999999
}
""");

            MqttGatewayOptions options = new MqttGatewayOptionsStore().LoadOrDefault(path, new MqttGatewayOptions());

            Assert.Equal("IPC-Gateway", options.GatewayId);
            Assert.Equal("IPC Gateway", options.GatewayName);
            Assert.Equal(string.Empty, options.SiteName);
            Assert.Equal("ipc.gateway.v1", options.CloudProtocolVersion);
            Assert.Equal("localhost", options.Host);
            Assert.Equal("IPC-Gateway", options.ClientId);
            Assert.Equal(string.Empty, options.Username);
            Assert.Equal(string.Empty, options.Password);
            Assert.Equal("ipc/write/#", options.SubscribeTopic);
            Assert.Equal("ipc/data/{device}/{group}/{tag}", options.PublishTopicTemplate);
            Assert.Equal("gateway/{gatewayId}/heartbeat", options.HeartbeatTopic);
            Assert.Equal("gateway/{gatewayId}/status", options.StatusTopic);
            Assert.Equal("gateway/{gatewayId}/reply/{requestId}", options.CommandReplyTopicTemplate);
            Assert.Equal("Data\\MqttOutbox", options.OutboxDirectory);
            Assert.Equal(1883, options.Port);
            Assert.Equal(2, options.PublishQos);
            Assert.Equal(0, options.HeartbeatQos);
            Assert.Equal(5, options.HeartbeatIntervalSeconds);
            Assert.Equal(5000, options.PublishAckTimeoutMilliseconds);
            Assert.Equal(86400, options.PublishUnchangedHeartbeatSeconds);
            Assert.Equal("Classic", options.PublishMode);
            Assert.Equal("spBv1.0", options.SparkplugNamespace);
            Assert.Equal("{group}/{tag}", options.SparkplugMetricNameTemplate);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MqttGatewayOptionsStore_SaveCreatesDirectoryAndReloads()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IPC.Gateway.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "nested", "mqtt.json");

        try
        {
            MqttGatewayOptionsStore store = new MqttGatewayOptionsStore();
            MqttGatewayOptions options = new MqttGatewayOptions
            {
                GatewayId = "edge-01",
                Host = " broker.local ",
                Port = 1884,
                PublishQos = 1
            };

            store.Save(path, options);
            MqttGatewayOptions loaded = store.LoadOrDefault(path, new MqttGatewayOptions());

            Assert.True(File.Exists(path));
            Assert.Equal("edge-01", loaded.GatewayId);
            Assert.Equal("broker.local", loaded.Host);
            Assert.Equal(1884, loaded.Port);
            Assert.Equal(1, loaded.PublishQos);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static void WriteMessage(string directory, string fileName, long id, string topic, DateTime createdAt)
    {
        MqttOutboxMessage message = new MqttOutboxMessage
        {
            Id = id,
            Topic = topic,
            Payload = "{}",
            Qos = 0,
            CreatedAt = createdAt
        };

        File.WriteAllText(Path.Combine(directory, fileName), message.ToFileText());
    }
}
