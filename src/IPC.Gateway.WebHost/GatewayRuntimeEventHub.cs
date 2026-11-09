using System.Threading.Channels;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimeEventHub
{
    private const int ClientQueueLimit = 4096;
    private const int RingBufferLimit = 4096;
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, Channel<GatewayRuntimeEventEnvelope>> _clients = new();
    private readonly Dictionary<Guid, GatewayRuntimeLatestQueue> _latestClients = new();
    private readonly GatewayRuntimeEventEnvelope[] _ring = new GatewayRuntimeEventEnvelope[RingBufferLimit];
    private int _ringStart;
    private int _ringCount;
    private long _sequence;

    public int SubscriberCount
    {
        get
        {
            lock (_syncRoot)
                return _clients.Count + _latestClients.Count;
        }
    }

    public long CurrentSequence
    {
        get
        {
            lock (_syncRoot)
                return _sequence;
        }
    }

    public GatewayRuntimeEventSubscription Subscribe(long afterSequence = 0)
    {
        Guid id = Guid.NewGuid();
        Channel<GatewayRuntimeEventEnvelope> channel = Channel.CreateBounded<GatewayRuntimeEventEnvelope>(
            new BoundedChannelOptions(ClientQueueLimit)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_syncRoot)
        {
            List<GatewayRuntimeEventEnvelope> replay = GetBufferedEventsAfterNoLock(afterSequence);
            foreach (GatewayRuntimeEventEnvelope envelope in replay)
                channel.Writer.TryWrite(envelope);

            _clients[id] = channel;
        }

        return new GatewayRuntimeEventSubscription(id, channel.Reader, () => Remove(id));
    }

    public GatewayRuntimeEventEnvelope Create(string type, object? data)
    {
        lock (_syncRoot)
            return CreateNoLock(type, data, ++_sequence);
    }

    // Browser subscriptions need current state, rather than a replay of every transition.
    public GatewayRuntimeEventSubscription SubscribeLatest(long afterSequence)
    {
        Guid id = Guid.NewGuid();
        var queue = new GatewayRuntimeLatestQueue();
        lock (_syncRoot)
        {
            foreach (var envelope in GetBufferedEventsAfterNoLock(afterSequence))
                queue.Publish(envelope);
            _latestClients[id] = queue;
        }
        return new GatewayRuntimeEventSubscription(id, queue.Reader, () => Remove(id), queue.Drain);
    }

    public GatewayRuntimeEventEnvelope Create(string type, object? data, long sequence)
    {
        return new GatewayRuntimeEventEnvelope
        {
            Sequence = Math.Max(0, sequence),
            Type = string.IsNullOrWhiteSpace(type) ? "message" : type.Trim(),
            Timestamp = DateTime.UtcNow,
            Data = data
        };
    }

    public void Publish(string type, object? data)
    {
        GatewayRuntimeEventEnvelope envelope;
        lock (_syncRoot)
        {
            envelope = CreateNoLock(type, data, ++_sequence);
            AddToRingNoLock(envelope);
            // Keep enqueue order consistent with sequence order across concurrent publishers.
            foreach (var channel in _clients.Values)
                channel.Writer.TryWrite(envelope);
            foreach (var queue in _latestClients.Values)
                queue.Publish(envelope);
        }
    }

    public IList<GatewayRuntimeEventEnvelope> GetBufferedEventsAfter(long afterSequence)
    {
        lock (_syncRoot)
            return GetBufferedEventsAfterNoLock(afterSequence);
    }

    private void Remove(Guid id)
    {
        Channel<GatewayRuntimeEventEnvelope>? channel;
        GatewayRuntimeLatestQueue? latest;
        lock (_syncRoot)
        {
            _clients.Remove(id, out channel);
            _latestClients.Remove(id, out latest);
        }

        channel?.Writer.TryComplete();
        latest?.Complete();
    }

    private GatewayRuntimeEventEnvelope CreateNoLock(string type, object? data, long sequence)
    {
        return new GatewayRuntimeEventEnvelope
        {
            Sequence = sequence,
            Type = string.IsNullOrWhiteSpace(type) ? "message" : type.Trim(),
            Timestamp = DateTime.UtcNow,
            Data = data
        };
    }

    private void AddToRingNoLock(GatewayRuntimeEventEnvelope envelope)
    {
        if (_ringCount < RingBufferLimit)
        {
            _ring[(_ringStart + _ringCount) % RingBufferLimit] = envelope;
            _ringCount++;
            return;
        }

        _ring[_ringStart] = envelope;
        _ringStart = (_ringStart + 1) % RingBufferLimit;
    }

    private List<GatewayRuntimeEventEnvelope> GetBufferedEventsAfterNoLock(long afterSequence)
    {
        List<GatewayRuntimeEventEnvelope> events = new List<GatewayRuntimeEventEnvelope>();
        for (int i = 0; i < _ringCount; i++)
        {
            GatewayRuntimeEventEnvelope envelope = _ring[(_ringStart + i) % RingBufferLimit];
            if (envelope != null && envelope.Sequence > afterSequence)
                events.Add(envelope);
        }

        return events;
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
