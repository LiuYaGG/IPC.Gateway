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
    private readonly Dictionary<string, TagValueSnapshotDto> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceRuntimeStatusDto> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GatewayRuntimeEventEnvelope> _other = new();
    private GatewayRuntimeEventEnvelope? _tagEvent, _deviceEvent, _statusEvent;
    private bool _completed;

    public ChannelReader<GatewayRuntimeEventEnvelope> Reader => _signal.Reader;

    public void Publish(GatewayRuntimeEventEnvelope envelope)
    {
        lock (_sync)
        {
            if (_completed) return;
            if (envelope.Type == "tags" && envelope.Data is GatewayRuntimeTagsChangedEvent tags)
            {
                foreach (var tag in tags.Tags)
                {
                    string key = string.Join("/", tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId);
                    if (!_tags.TryGetValue(key, out var previous) || tag.Timestamp >= previous.Timestamp)
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
        lock (_sync)
        {
            var result = new List<GatewayRuntimeEventEnvelope>(_other);
            if (_tagEvent != null)
            {
                var tags = _tags.Values.ToList();
                for (int offset = 0; offset < tags.Count; offset += 500)
                {
                    int count = Math.Min(500, tags.Count - offset);
                    result.Add(Copy(_tagEvent, new GatewayRuntimeTagsChangedEvent
                    {
                        Tags = tags.GetRange(offset, count), PendingCount = tags.Count - offset - count
                    }));
                }
            }
            if (_deviceEvent != null)
                result.Add(Copy(_deviceEvent, new GatewayRuntimeDevicesChangedEvent
                {
                    Devices = _devices.Values.ToList(), RemovedDeviceKeys = _removed.ToList()
                }));
            if (_statusEvent != null) result.Add(_statusEvent);
            Clear();
            return result.OrderBy(item => item.Sequence).ToList();
        }
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
        Sequence = source.Sequence, Timestamp = source.Timestamp, Type = source.Type, Data = data
    };
}
