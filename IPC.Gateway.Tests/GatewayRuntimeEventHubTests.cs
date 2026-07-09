using IPC.Gateway.WebHost;

namespace IPC.Gateway.Tests;

public sealed class GatewayRuntimeEventHubTests
{
    [Fact]
    public async Task Subscribe_ReplaysBufferedEventsAfterSequence()
    {
        GatewayRuntimeEventHub hub = new GatewayRuntimeEventHub();
        hub.Publish("tags", new { Value = 1 });
        hub.Publish("tags", new { Value = 2 });

        using GatewayRuntimeEventSubscription subscription = hub.Subscribe(1);

        GatewayRuntimeEventEnvelope replayed = await ReadNextAsync(subscription);

        Assert.Equal(2, replayed.Sequence);
        Assert.Equal("tags", replayed.Type);
    }

    [Fact]
    public async Task Publish_BuffersEventsWhenThereAreNoSubscribers()
    {
        GatewayRuntimeEventHub hub = new GatewayRuntimeEventHub();

        hub.Publish("tags", new { Value = 234 });

        using GatewayRuntimeEventSubscription subscription = hub.Subscribe();
        GatewayRuntimeEventEnvelope replayed = await ReadNextAsync(subscription);

        Assert.Equal(1, replayed.Sequence);
        Assert.Equal("tags", replayed.Type);
    }

    [Fact]
    public async Task Subscribe_DeliversReplayBeforeLiveEvents()
    {
        GatewayRuntimeEventHub hub = new GatewayRuntimeEventHub();
        hub.Publish("tags", new { Value = 1 });

        using GatewayRuntimeEventSubscription subscription = hub.Subscribe();
        hub.Publish("tags", new { Value = 2 });

        GatewayRuntimeEventEnvelope first = await ReadNextAsync(subscription);
        GatewayRuntimeEventEnvelope second = await ReadNextAsync(subscription);

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
    }

    private static async Task<GatewayRuntimeEventEnvelope> ReadNextAsync(GatewayRuntimeEventSubscription subscription)
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await subscription.Reader.ReadAsync(timeout.Token);
    }
}
