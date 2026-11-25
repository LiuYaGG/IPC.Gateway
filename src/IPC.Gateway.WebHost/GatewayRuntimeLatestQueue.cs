using System.Threading.Channels;
using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.WebHost;

// This queue belongs only to a browser subscription. It never feeds the runtime engine.
internal sealed class GatewayRuntimeLatestQueue
{
    private const int MaxPendingItems = 50000;
    private readonly object _sync = new();
    private readonly Channel<GatewayRuntimeEventEnvelope> _signal = Channel.CreateBounded<GatewayRuntimeEventEnvelope>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private Dictionary<string, TagValueSnapshotDto> _tags = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DeviceRuntimeStatusDto> _devices = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private List<GatewayRuntimeEventEnvelope> _other = new();
    private GatewayRuntimeEventEnvelope? _tagEvent, _deviceEvent, _statusEvent;
    private bool _completed;

    public GatewayRuntimeLatestQueue(bool includeDeviceData) => IncludeDeviceData = includeDeviceData;
    public bool IncludeDeviceData { get; }

    public ChannelReader<GatewayRuntimeEventEnvelope> Reader => _signal.Reader;

    public void Publish(GatewayRuntimeEventEnvelope envelope)
    {
        lock (_sync)
        {
            if (_completed) return;
            if (envelope.Type == "haChanged")
            {
                Clear();
                _other.Add(envelope);
            }
            else if (envelope.Type == "tags" && envelope.Data is GatewayRuntimeTagsChangedEvent tags)
            {
                foreach (var tag in tags.Tags)
                {
                    string key = string.Join("/", tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId);
                    if (!_tags.TryGetValue(key, out var previous) || (tag.RuntimeRevision > 0 || previous.RuntimeRevision > 0
                        ? tag.RuntimeRevision >= previous.RuntimeRevision : tag.Timestamp >= previous.Timestamp))
                        _tags[key] = tag;
                }
                _tagEvent = envelope;
            }
            else if (envelope.Type == "devices" && envelope.Data is GatewayRuntimeDevicesChangedEvent devices)
            {
                foreach (var device in devices.Devices)
                {
                    string key = "id:" + string.Join("/", device.ChannelId, device.DeviceId).Trim().ToLowerInvariant();
                    _devices[key] = device;
                    _removed.Remove(key);
                }
                foreach (string key in devices.RemovedDeviceKeys)
                {
                    _devices.Remove(key);
                    _removed.Add(key);
                }
                _deviceEvent = envelope;
            }
            else if (envelope.Type == "status" && envelope.Data is GatewayRuntimeStatusPatchEvent)
                _statusEvent = envelope;
            else
                _other.Add(envelope);

            if (_tags.Count + _devices.Count + _removed.Count + _other.Count > MaxPendingItems)
            {
                // A configuration that grows indefinitely must resync, never silently lose an identity.
                _completed = true;
                Clear();
                _signal.Writer.TryComplete();
                return;
            }
            _signal.Writer.TryWrite(envelope);
        }
    }

    public IReadOnlyList<GatewayRuntimeEventEnvelope> Drain()
    {
        Dictionary<string, TagValueSnapshotDto> tags;
        Dictionary<string, DeviceRuntimeStatusDto> devices;
        HashSet<string> removed;
        List<GatewayRuntimeEventEnvelope> result;
        GatewayRuntimeEventEnvelope? tagEvent, deviceEvent, statusEvent;
        lock (_sync)
        {
            tags = _tags; devices = _devices; removed = _removed; result = _other;
            tagEvent = _tagEvent; deviceEvent = _deviceEvent; statusEvent = _statusEvent;
            if (tagEvent != null) _tags = new(StringComparer.OrdinalIgnoreCase);
            if (deviceEvent != null)
            {
                _devices = new(StringComparer.OrdinalIgnoreCase);
                _removed = new(StringComparer.OrdinalIgnoreCase);
            }
            _other = [];
            _tagEvent = _deviceEvent = _statusEvent = null;
        }
        if (tagEvent != null)
        {
            int remaining = tags.Count;
            foreach (var batch in tags.Values.Chunk(500))
            {
                remaining -= batch.Length;
                result.Add(Copy(tagEvent, new GatewayRuntimeTagsChangedEvent { Tags = batch, PendingCount = remaining }));
            }
        }
        if (deviceEvent != null)
            result.Add(Copy(deviceEvent, new GatewayRuntimeDevicesChangedEvent
            {
                Devices = devices.Values.ToList(), RemovedDeviceKeys = removed.ToList()
            }));
        if (statusEvent != null) result.Add(statusEvent);
        return result.OrderBy(item => item.Sequence).ToList();
    }

    public void Complete()
    {
        lock (_sync)
        {
            _completed = true;
            Clear();
            _signal.Writer.TryComplete();
        }
    }

    private void Clear()
    {
        _tags.Clear(); _devices.Clear(); _removed.Clear(); _other.Clear();
        _tagEvent = _deviceEvent = _statusEvent = null;
    }

    private static GatewayRuntimeEventEnvelope Copy(GatewayRuntimeEventEnvelope source, object data) => new()
    {
        Sequence = source.Sequence, Timestamp = source.Timestamp, Type = source.Type, Data = data,
        ClusterId = source.ClusterId, Epoch = source.Epoch, SourceInstanceId = source.SourceInstanceId
    };
}
