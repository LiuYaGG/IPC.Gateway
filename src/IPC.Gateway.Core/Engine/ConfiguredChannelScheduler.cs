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
        private Dictionary<string, ChannelState> _channels = new Dictionary<string, ChannelState>(StringComparer.OrdinalIgnoreCase);

        public void Configure(ProjectConfig project)
        {
            Dictionary<string, ChannelState> replacement = new Dictionary<string, ChannelState>(StringComparer.OrdinalIgnoreCase);
            foreach (ChannelConfig channel in project.Channels ?? new List<ChannelConfig>())
            {
                if (channel == null || string.IsNullOrWhiteSpace(channel.Id))
                    continue;
                replacement[channel.Id] = new ChannelState(channel);
            }

            lock (_syncRoot)
                _channels = replacement;
        }

        public bool IsEnabled(DeviceConfig device)
        {
            ChannelState? state = Find(device);
            return state == null || state.Enabled;
        }

        public bool TryGetDispatchScore(DeviceConfig device, out double score, bool recoveryProbe = false)
        {
            score = double.MaxValue;
            ChannelState? state = Find(device);
            if (state == null)
            {
                score = 0D;
                return true;
            }

            SemaphoreSlim gate = recoveryProbe ? state.RecoveryGate : state.Gate;
            if (!state.Enabled || Volatile.Read(ref state.WaitingWrites) > 0 || gate.CurrentCount <= 0)
                return false;

            score = Volatile.Read(ref state.DispatchCount) / (double)state.Weight;
            return true;
        }

        public bool TryAcquirePoll(DeviceConfig device, out ConfiguredChannelLease? lease, bool recoveryProbe = false)
        {
            lease = null;
            ChannelState? state = Find(device);
            if (state == null)
            {
                lease = ConfiguredChannelLease.Empty;
                return true;
            }

            SemaphoreSlim gate = recoveryProbe ? state.RecoveryGate : state.Gate;
            if (!state.Enabled || Volatile.Read(ref state.WaitingWrites) > 0 || !gate.Wait(0))
                return false;

            Interlocked.Increment(ref state.DispatchCount);
            lease = new ConfiguredChannelLease(gate);
            return true;
        }

        public async ValueTask<ConfiguredChannelLease> AcquireWriteAsync(DeviceConfig device, CancellationToken cancellationToken)
        {
            ChannelState? state = Find(device);
            if (state == null)
                return ConfiguredChannelLease.Empty;
            if (!state.Enabled)
                throw new InvalidOperationException("The configured channel is disabled.");

            Interlocked.Increment(ref state.WaitingWrites);
            try
            {
                await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new ConfiguredChannelLease(state.Gate);
            }
            finally
            {
                Interlocked.Decrement(ref state.WaitingWrites);
            }
        }

        private ChannelState? Find(DeviceConfig? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ChannelId))
                return null;
            lock (_syncRoot)
                return _channels.TryGetValue(device.ChannelId, out ChannelState? state) ? state : null;
        }

        private sealed class ChannelState
        {
            public ChannelState(ChannelConfig channel)
            {
                Enabled = channel.Enabled;
                Weight = Math.Max(1, channel.SchedulingWeight);
                Gate = new SemaphoreSlim(Math.Max(1, channel.MaxConcurrentDevicePolls), Math.Max(1, channel.MaxConcurrentDevicePolls));
            }

            public bool Enabled { get; }
            public int Weight { get; }
            public SemaphoreSlim Gate { get; }
            public SemaphoreSlim RecoveryGate { get; } = new SemaphoreSlim(1, 1);
            public long DispatchCount;
            public int WaitingWrites;
        }
    }

    internal sealed class ConfiguredChannelLease : IDisposable
    {
        public static ConfiguredChannelLease Empty => new ConfiguredChannelLease(null);
        private SemaphoreSlim? _gate;

        public ConfiguredChannelLease(SemaphoreSlim? gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            SemaphoreSlim? gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
