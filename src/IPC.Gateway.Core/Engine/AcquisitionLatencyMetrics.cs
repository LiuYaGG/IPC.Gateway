using System;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    /// <summary>
    /// Fixed-memory latency samples. Percentiles describe the most recent N valid
    /// observations, not a time window or a promise about every individual device.
    /// </summary>
    internal sealed class AcquisitionLatencyMetrics
    {
        private readonly object _syncRoot = new object();
        private readonly SampleRing _initialRead;
        private readonly SampleRing _healthyRefresh;
        private readonly SampleRing _tagProbeQueue;
        private readonly SampleRing _deviceProbeQueue;
        private long _rejectedSampleCount;

        public AcquisitionLatencyMetrics(int sampleCapacity = 2048)
        {
            if (sampleCapacity <= 0 || sampleCapacity > 65536)
                throw new ArgumentOutOfRangeException(nameof(sampleCapacity), "Sample capacity must be between 1 and 65536.");
            SampleCapacity = sampleCapacity;
            _initialRead = new SampleRing(sampleCapacity);
            _healthyRefresh = new SampleRing(sampleCapacity);
            _tagProbeQueue = new SampleRing(sampleCapacity);
            _deviceProbeQueue = new SampleRing(sampleCapacity);
        }

        public int SampleCapacity { get; }

        public void RecordInitialReadLatencyMs(double milliseconds) => Record(_initialRead, milliseconds);
        public void RecordHealthyRefreshGapMs(double milliseconds) => Record(_healthyRefresh, milliseconds);
        public void RecordTagProbeQueueDelayMs(double milliseconds) => Record(_tagProbeQueue, milliseconds);
        public void RecordDeviceProbeQueueDelayMs(double milliseconds) => Record(_deviceProbeQueue, milliseconds);

        private void Record(SampleRing ring, double milliseconds)
        {
            lock (_syncRoot)
            {
                if (!double.IsFinite(milliseconds) || milliseconds < 0)
                {
                    if (_rejectedSampleCount < long.MaxValue) _rejectedSampleCount++;
                    return;
                }
                ring.Record(milliseconds);
            }
        }

        public RuntimeAcquisitionLatencyStats Snapshot()
        {
            SampleWindow initial, healthy, tagProbe, deviceProbe;
            long rejected;
            DateTime sampleTime;
            lock (_syncRoot)
            {
                initial = _initialRead.Copy();
                healthy = _healthyRefresh.Copy();
                tagProbe = _tagProbeQueue.Copy();
                deviceProbe = _deviceProbeQueue.Copy();
                rejected = _rejectedSampleCount;
                sampleTime = DateTime.UtcNow;
            }
            // Sorting and percentile calculation never block the acquisition writer.
            return new RuntimeAcquisitionLatencyStats
            {
                SampleCapacity = SampleCapacity,
                SampleTimeUtc = sampleTime,
                RejectedSampleCount = rejected,
                InitialReadLatencyMs = Describe(initial),
                HealthyRefreshGapMs = Describe(healthy),
                TagProbeQueueDelayMs = Describe(tagProbe),
                DeviceProbeQueueDelayMs = Describe(deviceProbe)
            };
        }

        public void Reset()
        {
            lock (_syncRoot)
            {
                _initialRead.Reset();
                _healthyRefresh.Reset();
                _tagProbeQueue.Reset();
                _deviceProbeQueue.Reset();
                _rejectedSampleCount = 0;
            }
        }

        private static RuntimeLatencyDistribution Describe(SampleWindow window)
        {
            double[] values = window.Values;
            var result = new RuntimeLatencyDistribution { Count = window.Count, WindowCount = values.Length, LastMs = window.Last };
            if (values.Length == 0) return result;
            Array.Sort(values);
            double average = 0;
            // Incremental mean stays finite even for large, otherwise valid samples.
            for (int i = 0; i < values.Length; i++) average += (values[i] - average) / (i + 1);
            result.AverageMs = average;
            result.P95Ms = values[Math.Max(0, (int)Math.Ceiling(values.Length * 0.95) - 1)];
            result.P99Ms = values[Math.Max(0, (int)Math.Ceiling(values.Length * 0.99) - 1)];
            result.MaxMs = values[^1];
            return result;
        }

        private readonly record struct SampleWindow(double[] Values, long Count, double Last);

        // Accessed only under the enclosing metrics lock.
        private sealed class SampleRing(int capacity)
        {
            private readonly double[] _values = new double[capacity];
            private int _next;
            private int _length;
            private long _count;
            private double _last;

            public void Record(double value)
            {
                _values[_next] = value;
                _next = (_next + 1) % _values.Length;
                if (_length < _values.Length) _length++;
                if (_count < long.MaxValue) _count++;
                _last = value;
            }

            public SampleWindow Copy()
            {
                var values = new double[_length];
                Array.Copy(_values, values, _length);
                return new SampleWindow(values, _count, _last);
            }

            public void Reset() { _next = _length = 0; _count = 0; _last = 0; }
        }
    }
}
