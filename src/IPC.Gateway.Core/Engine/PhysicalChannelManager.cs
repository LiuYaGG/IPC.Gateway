using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    internal sealed class PhysicalChannelManager
    {
        private readonly ConcurrentDictionary<string, PhysicalChannelState> _channels =
            new ConcurrentDictionary<string, PhysicalChannelState>(StringComparer.OrdinalIgnoreCase);

        public async ValueTask<PhysicalChannelLease> AcquireAsync(DeviceConfig? device, CancellationToken cancellationToken)
        {
            string key = BuildChannelKey(device);
            PhysicalChannelState state = _channels.GetOrAdd(key, static channelKey => new PhysicalChannelState(channelKey));
            await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PhysicalChannelLease(state);
        }

        public PhysicalChannelSnapshot GetSnapshot(DeviceConfig device)
        {
            string key = BuildChannelKey(device);
            return _channels.TryGetValue(key, out PhysicalChannelState? state)
                ? state.Snapshot()
                : new PhysicalChannelSnapshot(key, "Unknown", 0, DateTime.MinValue, DateTime.MinValue, string.Empty);
        }

        public static string BuildChannelKey(DeviceConfig? device)
        {
            if (device == null)
                return "device|unknown";

            PlcConnectionOptions connection = device.Connection ?? new PlcConnectionOptions();
            string host = (connection.Host ?? string.Empty).Trim().ToUpperInvariant();
            string endpoint = host + ":" + connection.Port.ToString(CultureInfo.InvariantCulture);

            if (connection.Transport == NetworkTransport.Udp)
                return "udp|" + endpoint;

            if (IsSerialOrBusProtocol(device.Protocol))
            {
                return "bus|" + endpoint + "|" + connection.DataBits.ToString(CultureInfo.InvariantCulture) +
                       "|" + connection.SerialParity + "|" + connection.SerialStopBits;
            }

            if (device.Protocol == PlcProtocol.Dlt6452007 ||
                device.Protocol == PlcProtocol.Cjt1882004 ||
                device.Protocol == PlcProtocol.Cjt1882018)
                return "meter|" + connection.Transport + "|" + endpoint;

            if (device.Protocol == PlcProtocol.OpcDa)
                return "opcda|" + host + "|" + (connection.OpcDaServerProgId ?? string.Empty).Trim().ToUpperInvariant();

            string deviceKey = string.IsNullOrWhiteSpace(device.Id) ? device.Name ?? string.Empty : device.Id;
            return "device|" + deviceKey;
        }

        private static bool IsSerialOrBusProtocol(PlcProtocol protocol)
        {
            return protocol == PlcProtocol.ModbusRtu ||
                   protocol == PlcProtocol.ModbusAscii ||
                   protocol == PlcProtocol.MitsubishiSerial ||
                   protocol == PlcProtocol.MitsubishiQlSerial ||
                   protocol == PlcProtocol.CanOpen;
        }

        internal sealed class PhysicalChannelState
        {
            private readonly object _syncRoot = new object();

            public PhysicalChannelState(string key)
            {
                Key = key;
                Gate = new SemaphoreSlim(1, 1);
                LastError = string.Empty;
            }

            public string Key { get; }
            public SemaphoreSlim Gate { get; }
            public int ConsecutiveFailures { get; private set; }
            public DateTime LastSuccessUtc { get; private set; }
            public DateTime LastFailureUtc { get; private set; }
            public string LastError { get; private set; }

            public void RecordSuccess()
            {
                lock (_syncRoot)
                {
                    ConsecutiveFailures = 0;
                    LastSuccessUtc = DateTime.UtcNow;
                    LastError = string.Empty;
                }
            }

            public void RecordFailure(string message)
            {
                lock (_syncRoot)
                {
                    ConsecutiveFailures++;
                    LastFailureUtc = DateTime.UtcNow;
                    LastError = message ?? string.Empty;
                }
            }

            public PhysicalChannelSnapshot Snapshot()
            {
                lock (_syncRoot)
                {
                    string status = ConsecutiveFailures >= 3
                        ? "Offline"
                        : ConsecutiveFailures > 0
                            ? "Degraded"
                            : LastSuccessUtc == DateTime.MinValue ? "Unknown" : "Healthy";
                    return new PhysicalChannelSnapshot(
                        Key,
                        status,
                        ConsecutiveFailures,
                        LastSuccessUtc,
                        LastFailureUtc,
                        LastError);
                }
            }
        }
    }

    internal sealed class PhysicalChannelLease : IDisposable
    {
        private PhysicalChannelManager.PhysicalChannelState? _state;

        internal PhysicalChannelLease(PhysicalChannelManager.PhysicalChannelState state)
        {
            _state = state;
        }

        public void RecordSuccess()
        {
            _state?.RecordSuccess();
        }

        public void RecordFailure(string message)
        {
            _state?.RecordFailure(message);
        }

        public void Dispose()
        {
            PhysicalChannelManager.PhysicalChannelState? state = Interlocked.Exchange(ref _state, null);
            state?.Gate.Release();
        }
    }

    internal sealed record PhysicalChannelSnapshot(
        string Key,
        string Status,
        int ConsecutiveFailures,
        DateTime LastSuccessUtc,
        DateTime LastFailureUtc,
        string LastError);
}
