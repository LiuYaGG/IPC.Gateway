using System.Threading.Channels;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimeEventHub
{
    private readonly object _subscriptionsLock = new();
    private readonly object _publishLock = new();
    private GatewayRuntimeLatestQueue[] _subscribers = [];
    private int _deviceSubscribers;
    private long _sequence;

    public int SubscriberCount => Volatile.Read(ref _subscribers).Length;
    public bool HasDeviceSubscribers => Volatile.Read(ref _deviceSubscribers) != 0;
    public long CurrentSequence => Interlocked.Read(ref _sequence);

    // Register and capture the watermark atomically. New connections always receive
    // an authoritative snapshot, so events from before registration need no replay log.
    public GatewayRuntimeEventSubscription SubscribeLatest(out long snapshotSequence, bool includeDeviceData)
    {
        var queue = new GatewayRuntimeLatestQueue(includeDeviceData);
        lock (_subscriptionsLock)
        {
            if (includeDeviceData) Interlocked.Increment(ref _deviceSubscribers);
            Volatile.Write(ref _subscribers, [.. _subscribers, queue]);
            snapshotSequence = _sequence;
        }
        return new GatewayRuntimeEventSubscription(Guid.NewGuid(), queue.Reader, () => Remove(queue), queue.Drain);
    }

    public GatewayRuntimeEventEnvelope Create(string type, object? data, long sequence) => new()
    {
        Sequence = sequence,
        Type = type,
        Timestamp = DateTime.UtcNow,
        Data = data
    };

    public void Publish(string type, object? data)
    {
        // Preserve order across publishers without blocking subscriptions, disconnects
        // or acquisition callbacks on a slow subscriber's merge.
        lock (_publishLock)
        {
            GatewayRuntimeLatestQueue[] subscribers;
            GatewayRuntimeEventEnvelope envelope;
            lock (_subscriptionsLock)
            {
                subscribers = _subscribers;
                if (subscribers.Length == 0) return;
                envelope = Create(type, data, Interlocked.Increment(ref _sequence));
            }
            foreach (var queue in subscribers)
                if (queue.IncludeDeviceData || type is not ("tags" or "devices"))
                    queue.Publish(envelope);
        }
    }

    private void Remove(GatewayRuntimeLatestQueue queue)
    {
        lock (_subscriptionsLock)
        {
            if (!_subscribers.Contains(queue)) return;
            Volatile.Write(ref _subscribers, _subscribers.Where(item => item != queue).ToArray());
            if (queue.IncludeDeviceData) Interlocked.Decrement(ref _deviceSubscribers);
        }
        queue.Complete();
    }
}

public sealed class GatewayRuntimeEventSubscription : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;
    private readonly Func<IReadOnlyList<GatewayRuntimeEventEnvelope>>? _drain;

    public GatewayRuntimeEventSubscription(Guid id, ChannelReader<GatewayRuntimeEventEnvelope> reader, Action dispose,
        Func<IReadOnlyList<GatewayRuntimeEventEnvelope>>? drain = null)
    {
        Id = id;
        Reader = reader;
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _drain = drain;
    }

    public Guid Id { get; }
    public ChannelReader<GatewayRuntimeEventEnvelope> Reader { get; }

    public IReadOnlyList<GatewayRuntimeEventEnvelope> DrainPendingEvents() =>
        _drain?.Invoke() ?? throw new InvalidOperationException("This subscription is not a latest-state subscription.");

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _dispose();
    }
}

public sealed class GatewayRuntimeEventEnvelope
{
    public long Sequence { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public object? Data { get; set; }
}
