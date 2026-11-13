using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    internal sealed class ConfiguredChannelScheduler
    {
        private readonly object _syncRoot = new object();
        // Keep the accounting object across configuration generations, including
        // delete/re-add, until every old lease has returned to this same object.
        private readonly Dictionary<string, ChannelState> _channels = new Dictionary<string, ChannelState>(StringComparer.OrdinalIgnoreCase);

        public void Configure(ProjectConfig project)
        {
            lock (_syncRoot)
            {
                var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ChannelConfig channel in project.Channels ?? new List<ChannelConfig>())
                {
                    if (channel == null || string.IsNullOrWhiteSpace(channel.Id)) continue;
                    retained.Add(channel.Id);
                    if (!_channels.TryGetValue(channel.Id, out ChannelState? state))
                        _channels[channel.Id] = state = new ChannelState();
                    state.Configure(channel.Enabled, channel.MaxConcurrentDevicePolls, channel.SchedulingWeight);
                }
                foreach (KeyValuePair<string, ChannelState> pair in _channels)
                    if (!retained.Contains(pair.Key)) pair.Value.Configure(false, 1, 1);
            }
        }

        public bool IsEnabled(DeviceConfig device) => Find(device)?.IsEnabled ?? true;

        public bool TryGetDispatchScore(DeviceConfig device, out double score, bool recoveryProbe = false)
        {
            ChannelState? state = Find(device);
            if (state != null) return state.TryGetDispatchScore(device.Id, recoveryProbe, out score);
            score = 0D;
            return true;
        }

        public bool TryAcquirePoll(DeviceConfig device, out ConfiguredChannelLease? lease, bool recoveryProbe = false)
        {
            ChannelState? state = Find(device);
            if (state != null) return state.TryAcquirePoll(device.Id, recoveryProbe, out lease);
            lease = ConfiguredChannelLease.Empty;
            return true;
        }

        public ValueTask<ConfiguredChannelLease> AcquireWriteAsync(DeviceConfig device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChannelState? state = Find(device);
            return state == null ? ValueTask.FromResult(ConfiguredChannelLease.Empty) : state.AcquireWriteAsync(device.Id, cancellationToken);
        }

        private ChannelState? Find(DeviceConfig? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ChannelId)) return null;
            lock (_syncRoot) return _channels.TryGetValue(device.ChannelId, out ChannelState? state) ? state : null;
        }

        private sealed class ChannelState
        {
            private const int WriteQueueCapacity = 256;
            private const int MaxWriteBurst = 2;
            private const int PollReservationMs = 1000;
            private readonly object _syncRoot = new object();
            // Polls and writes reserve the device and channel together, before entering
            // the actor. Only the FIFO head of an idle device competes for channel time.
            private readonly HashSet<string> _activeDevices = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, DeviceWriteQueue> _deviceWrites = new(StringComparer.OrdinalIgnoreCase);
            private readonly LinkedList<DeviceWriteQueue> _readyWrites = new();
            private int _pendingWriteCount;
            private bool _enabled;
            private int _limit = 1;
            private int _weight = 1;
            private int _activeRecovery;
            private int _consecutiveWrites;
            private bool _recoverySinceNormal;
            private readonly Dictionary<string, DateTime> _pollDemand = new(StringComparer.OrdinalIgnoreCase);
            private DateTime _normalDemandUntilUtc;
            private long _dispatchCount;
            private Timer? _reservationTimer;

            public bool IsEnabled { get { lock (_syncRoot) return _enabled; } }

            public void Configure(bool enabled, int limit, int weight)
            {
                lock (_syncRoot)
                {
                    _enabled = enabled;
                    _limit = Math.Max(1, limit);
                    _weight = Math.Max(1, weight);
                    _pollDemand.Clear();
                    if (enabled)
                    {
                        // Decreasing a limit does not revoke executing operations;
                        // admission stays closed until the existing leases drain.
                        GrantWritesNoLock();
                        return;
                    }
                    _normalDemandUntilUtc = DateTime.MinValue;
                    _reservationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    foreach (DeviceWriteQueue queue in _deviceWrites.Values)
                    {
                        foreach (WriteWaiter waiter in queue.Writes)
                        {
                            waiter.Node = null;
                            waiter.Registration.Unregister();
                            waiter.Completion.TrySetException(new InvalidOperationException("The configured channel is disabled."));
                        }
                    }
                    _readyWrites.Clear();
                    _deviceWrites.Clear();
                    _pendingWriteCount = 0;
                }
            }

            public bool TryGetDispatchScore(string deviceId, bool recovery, out double score)
            {
                lock (_syncRoot)
                {
                    score = double.MaxValue;
                    RegisterPollDemandNoLock(deviceId, recovery);
                    if (!CanAcquirePollNoLock(deviceId, recovery)) return false;
                    score = _dispatchCount / (double)_weight;
                    return true;
                }
            }

            public bool TryAcquirePoll(string deviceId, bool recovery, out ConfiguredChannelLease? lease)
            {
                lock (_syncRoot)
                {
                    lease = null;
                    RegisterPollDemandNoLock(deviceId, recovery);
                    if (!CanAcquirePollNoLock(deviceId, recovery)) return false;
                    _activeDevices.Add(deviceId);
                    if (_deviceWrites.TryGetValue(deviceId, out DeviceWriteQueue? queue))
                    {
                        RemoveReadyNoLock(queue);
                        queue.ConsecutiveWrites = 0;
                        queue.PollDemandUntilUtc = DateTime.MinValue;
                    }
                    if (recovery) _activeRecovery++;
                    _dispatchCount++;
                    _consecutiveWrites = 0;
                    _pollDemand.Remove(deviceId);
                    if (recovery) _recoverySinceNormal = true;
                    else
                    {
                        _recoverySinceNormal = false;
                        _normalDemandUntilUtc = DateTime.MinValue;
                    }
                    lease = new ConfiguredChannelLease(() => Release(deviceId, recovery));
                    GrantWritesNoLock();
                    return true;
                }
            }

            private void RegisterPollDemandNoLock(string deviceId, bool recovery)
            {
                if (!_enabled || recovery && _activeRecovery != 0) return;
                DateTime until = DateTime.UtcNow.AddMilliseconds(PollReservationMs);
                _pollDemand[deviceId] = until;
                if (!recovery) _normalDemandUntilUtc = until;
                if (_deviceWrites.TryGetValue(deviceId, out DeviceWriteQueue? queue))
                    queue.PollDemandUntilUtc = until;
            }

            private bool CanAcquirePollNoLock(string deviceId, bool recovery)
            {
                if (!_enabled || _activeDevices.Count >= _limit || _activeDevices.Contains(deviceId) || recovery && _activeRecovery != 0) return false;
                // The runtime checks recovery first. At a saturated channel a normal
                // poll must get a turn between repeated recovery probes.
                if (recovery && _recoverySinceNormal && DateTime.UtcNow < _normalDemandUntilUtc) return false;
                return _readyWrites.Count == 0 || _consecutiveWrites >= MaxWriteBurst ||
                    (_deviceWrites.TryGetValue(deviceId, out DeviceWriteQueue? queue) && queue.ConsecutiveWrites >= MaxWriteBurst);
            }

            public ValueTask<ConfiguredChannelLease> AcquireWriteAsync(string deviceId, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var waiter = new WriteWaiter(deviceId, token);
                lock (_syncRoot)
                {
                    if (!_enabled) throw new InvalidOperationException("The configured channel is disabled.");
                    if (_pendingWriteCount >= WriteQueueCapacity)
                        throw new InvalidOperationException("The configured channel write queue is full (capacity " + WriteQueueCapacity + ").");
                    if (!_deviceWrites.TryGetValue(deviceId, out DeviceWriteQueue? queue))
                    {
                        queue = new DeviceWriteQueue(deviceId);
                        _deviceWrites.Add(deviceId, queue);
                        if (!_activeDevices.Contains(deviceId))
                            queue.ReadyNode = _readyWrites.AddLast(queue);
                    }
                    waiter.Node = queue.Writes.AddLast(waiter);
                    _pendingWriteCount++;
                    GrantWritesNoLock();
                }
                if (token.CanBeCanceled)
                {
                    CancellationTokenRegistration registration = token.UnsafeRegister(state => CancelWrite((WriteWaiter)state!), waiter);
                    bool keep;
                    lock (_syncRoot)
                    {
                        keep = waiter.Node != null;
                        if (keep) waiter.Registration = registration;
                    }
                    if (!keep) registration.Unregister();
                }
                return new ValueTask<ConfiguredChannelLease>(waiter.Completion.Task);
            }

            private void CancelWrite(WriteWaiter waiter)
            {
                bool removed = false;
                lock (_syncRoot)
                {
                    if (waiter.Node != null)
                    {
                        RemoveWriteNoLock(_deviceWrites[waiter.DeviceId], waiter);
                        removed = true;
                        GrantWritesNoLock();
                    }
                }
                if (removed)
                {
                    waiter.Completion.TrySetCanceled(waiter.Token);
                }
            }

            private void Release(string deviceId, bool recovery)
            {
                lock (_syncRoot)
                {
                    _activeDevices.Remove(deviceId);
                    if (_deviceWrites.TryGetValue(deviceId, out DeviceWriteQueue? queue))
                    {
                        if (queue.Writes.Count == 0) _deviceWrites.Remove(deviceId);
                        else queue.ReadyNode = _readyWrites.AddLast(queue);
                    }
                    if (recovery) _activeRecovery--;
                    GrantWritesNoLock();
                }
            }

            private void RemoveReadyNoLock(DeviceWriteQueue queue)
            {
                if (queue.ReadyNode == null) return;
                _readyWrites.Remove(queue.ReadyNode);
                queue.ReadyNode = null;
            }

            private void RemoveWriteNoLock(DeviceWriteQueue queue, WriteWaiter waiter)
            {
                queue.Writes.Remove(waiter.Node!);
                waiter.Node = null;
                _pendingWriteCount--;
                if (queue.Writes.Count == 0)
                {
                    RemoveReadyNoLock(queue);
                    if (!_activeDevices.Contains(queue.DeviceId)) _deviceWrites.Remove(queue.DeviceId);
                }
                // Nonblocking unregister is safe even if cancellation is already
                // trying to take this lock; Dispose here could deadlock.
                waiter.Registration.Unregister();
            }

            private void GrantWritesNoLock()
            {
                while (_enabled && _activeDevices.Count < _limit && _readyWrites.First != null)
                {
                    DateTime now = DateTime.UtcNow;
                    if (_consecutiveWrites >= MaxWriteBurst)
                    {
                        // A busy device cannot use a second channel slot. Reserve only
                        // for a poll which could actually start on the available slot.
                        foreach (var demand in _pollDemand)
                        {
                            if (demand.Value <= now || _activeDevices.Contains(demand.Key)) continue;
                            RetryAfterReservationNoLock(demand.Value, now);
                            return;
                        }
                    }
                    // Another device's successful poll resets the channel burst counter.
                    // Still guarantee a read turn locally on this device under a write flood.
                    DateTime earliestExpiry = DateTime.MaxValue;
                    var node = _readyWrites.First;
                    while (node != null && node.Value.ConsecutiveWrites >= MaxWriteBurst && now < node.Value.PollDemandUntilUtc)
                    {
                        if (node.Value.PollDemandUntilUtc < earliestExpiry) earliestExpiry = node.Value.PollDemandUntilUtc;
                        node = node.Next;
                    }
                    if (node == null)
                    {
                        RetryAfterReservationNoLock(earliestExpiry, now);
                        return;
                    }
                    DeviceWriteQueue queue = node.Value;
                    WriteWaiter waiter = queue.Writes.First!.Value;
                    if (waiter.Token.IsCancellationRequested)
                    {
                        RemoveWriteNoLock(queue, waiter);
                        waiter.Completion.TrySetCanceled(waiter.Token);
                        continue;
                    }
                    _activeDevices.Add(queue.DeviceId);
                    RemoveReadyNoLock(queue);
                    RemoveWriteNoLock(queue, waiter);
                    queue.ConsecutiveWrites = Math.Min(MaxWriteBurst, queue.ConsecutiveWrites + 1);
                    _consecutiveWrites = Math.Min(MaxWriteBurst, _consecutiveWrites + 1);
                    _dispatchCount++;
                    waiter.Completion.TrySetResult(new ConfiguredChannelLease(() => Release(queue.DeviceId, false)));
                }
            }

            private void RetryAfterReservationNoLock(DateTime expiry, DateTime now)
            {
                // A vanished poll must not leave writes blocked indefinitely.
                _reservationTimer ??= new Timer(_ => { lock (_syncRoot) GrantWritesNoLock(); });
                _reservationTimer.Change(Math.Max(1, (int)Math.Ceiling((expiry - now).TotalMilliseconds)), Timeout.Infinite);
            }

            private sealed class DeviceWriteQueue(string deviceId)
            {
                public string DeviceId { get; } = deviceId;
                public LinkedList<WriteWaiter> Writes { get; } = new();
                public LinkedListNode<DeviceWriteQueue>? ReadyNode;
                public int ConsecutiveWrites;
                public DateTime PollDemandUntilUtc;
            }

            private sealed class WriteWaiter(string deviceId, CancellationToken token)
            {
                public string DeviceId { get; } = deviceId;
                public CancellationToken Token { get; } = token;
                public TaskCompletionSource<ConfiguredChannelLease> Completion { get; } = new TaskCompletionSource<ConfiguredChannelLease>(TaskCreationOptions.RunContinuationsAsynchronously);
                public LinkedListNode<WriteWaiter>? Node;
                public CancellationTokenRegistration Registration;
            }
        }
    }

    internal sealed class ConfiguredChannelLease : IDisposable
    {
        public static ConfiguredChannelLease Empty => new ConfiguredChannelLease(null);
        private Action? _release;
        public ConfiguredChannelLease(Action? release) => _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
