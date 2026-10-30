using System.Reflection;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.WebHost;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPC.Gateway.Tests;

public sealed class GatewayRuntimePushHostedServiceTests
{
    [Fact]
    public void Flush_Drains1501TagsInBoundedEventsWithoutLeavingStaleDirectTag()
    {
        using var scope = new PushScope();
        using var subscription = scope.Hub.Subscribe();
        for (int cycle = 1; cycle <= 4; cycle++)
        {
            scope.Enqueue("direct-id", "", "DirectAlias", cycle);
            for (int i = 0; i < 1500; i++)
                scope.Enqueue("group-tag-" + i, "group", "GroupedTag" + i, cycle);
            scope.Flush();

            var events = ReadAll(subscription);
            Assert.Equal(4, events.Count);
            Assert.All(events, batch => Assert.InRange(batch.Tags.Count, 1, 500));
            Assert.Equal(new[] { 1001, 501, 1, 0 }, events.Select(batch => batch.PendingCount));
            var tags = events.SelectMany(batch => batch.Tags).ToList();
            Assert.Equal(1501, tags.Count);
            Assert.Equal(1501, tags.Select(tag => tag.TagId).Distinct().Count());
            Assert.All(tags, tag => Assert.Equal(cycle.ToString(), tag.ValueText));
            Assert.Single(tags, tag => tag.TagId == "direct-id" && tag.GroupId == "");
            Assert.Single(tags, tag => tag.TagId == "group-tag-1499");
        }
        scope.Flush();
        Assert.Empty(ReadAll(subscription));
    }

    [Fact]
    public void Flush_CoalescesLatestValuesByFullIdentityAndKeepsDirectAndGroupedAliases()
    {
        using var scope = new PushScope();
        using var subscription = scope.Hub.Subscribe();
        scope.Enqueue("direct", "", "Different direct name", 1);
        scope.Enqueue("grouped", "group", "Different grouped name", 2);
        scope.Enqueue("direct", "", "Different direct name", 3);
        scope.Enqueue("grouped", "group", "Different grouped name", 4);
        scope.Flush();

        var tags = Assert.Single(ReadAll(subscription)).Tags;
        Assert.Equal(2, tags.Count);
        Assert.Equal("3", Assert.Single(tags, tag => tag.TagId == "direct").ValueText);
        Assert.Equal("4", Assert.Single(tags, tag => tag.TagId == "grouped").ValueText);
    }

    [Fact]
    public void Flush_SameTagIdInDifferentGroupScopesRemainsIndependent()
    {
        using var scope = new PushScope();
        using var subscription = scope.Hub.Subscribe();
        scope.Enqueue("shared-id", "", "SameName", 11);
        scope.Enqueue("shared-id", "group", "SameName", 22);
        scope.Flush();

        var tags = Assert.Single(ReadAll(subscription)).Tags;
        Assert.Equal(2, tags.Count);
        Assert.Equal("11", Assert.Single(tags, tag => tag.GroupId == "").ValueText);
        Assert.Equal("22", Assert.Single(tags, tag => tag.GroupId == "group").ValueText);
    }

    private static List<GatewayRuntimeTagsChangedEvent> ReadAll(GatewayRuntimeEventSubscription subscription)
    {
        var batches = new List<GatewayRuntimeTagsChangedEvent>();
        while (subscription.Reader.TryRead(out var envelope))
        {
            Assert.Equal("tags", envelope.Type);
            batches.Add(Assert.IsType<GatewayRuntimeTagsChangedEvent>(envelope.Data));
        }
        return batches;
    }

    private sealed class PushScope : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ipc-gateway-push-tests", Guid.NewGuid().ToString("N"));
        private readonly GatewayCoreService _gateway;
        private readonly GatewayRuntimePushHostedService _service;
        private static readonly MethodInfo EnqueueMethod = typeof(GatewayRuntimePushHostedService).GetMethod("OnTagValueChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo FlushMethod = typeof(GatewayRuntimePushHostedService).GetMethod("FlushPendingTags", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public GatewayRuntimeEventHub Hub { get; } = new();

        public PushScope()
        {
            Directory.CreateDirectory(_directory);
            _gateway = new GatewayCoreService(
                new GatewayRuntimeOptions
                {
                    Database = new GatewayDatabaseOptions
                    {
                        Provider = "Sqlite",
                        Database = Path.Combine(_directory, "gateway.db"),
                        ConnectionString = "Data Source=" + Path.Combine(_directory, "gateway.db") + ";Pooling=False",
                        AutoCreateDatabase = true
                    }
                },
                new MqttGatewayOptions { Enabled = false, OutboxDirectory = Path.Combine(_directory, "outbox") },
                new OpcUaServerOptions { Enabled = false },
                new LocalHistoryOptions { Enabled = false, Directory = Path.Combine(_directory, "history") },
                new StorageHealthThresholds());
            _service = new GatewayRuntimePushHostedService(_gateway, Hub, NullLogger<GatewayRuntimePushHostedService>.Instance);
        }

        public void Enqueue(string id, string group, string name, int value) =>
            EnqueueMethod.Invoke(_service, new object?[] { null, new TagValueChangedEventArgs(new TagValueSnapshot
            {
                ChannelId = "channel", DeviceId = "device", GroupId = group, TagId = id, TagName = name,
                ValueText = value.ToString(), RawValueText = value.ToString(), Timestamp = DateTime.UtcNow, Quality = TagQuality.Good
            }) });

        public void Flush() => FlushMethod.Invoke(_service, null);

        public void Dispose()
        {
            _service.Dispose();
            _gateway.Dispose();
            string allowedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ipc-gateway-push-tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(_directory).StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected test cleanup path");
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
