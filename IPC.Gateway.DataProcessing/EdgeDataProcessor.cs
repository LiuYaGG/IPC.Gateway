/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeDataProcessor
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.DataProcessing
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System.Globalization;

namespace IPC.Gateway.DataProcessing;

public sealed class EdgeDataProcessor
{
    private readonly object _syncRoot;
    private readonly Dictionary<string, EdgeTagProcessingState> _states;
    private readonly EdgeDataProcessingOptions _options;
    private readonly EdgeDataProcessingStats _stats;

    public EdgeDataProcessor(EdgeDataProcessingOptions? options)
    {
        _syncRoot = new object();
        _states = new Dictionary<string, EdgeTagProcessingState>(StringComparer.OrdinalIgnoreCase);
        _options = EdgeDataProcessingOptions.Normalize(options);
        _stats = new EdgeDataProcessingStats();
    }

    public EdgeDataProcessingOptions Options => _options.Clone();

    public EdgeDataProcessingStats GetStats()
    {
        lock (_syncRoot)
            return _stats.Clone();
    }

    public EdgeDataProcessingResult Process(EdgeDataPoint input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        EdgeDataPoint point = NormalizeInput(input);
        EdgeDataProcessingResult result = new EdgeDataProcessingResult();
        lock (_syncRoot)
        {
            _stats.ReceivedValueCount++;
            EdgeTagProcessingState state = GetState(point.TagKey);
            AddAggregatePoints(state, point, result);

            EdgeProcessedDataPoint current = CreateCurrent(point);
            AddFillPoints(state, current, result);

            if (ShouldDownsample(state, current))
            {
                result.DownsamplingSkipped = true;
                result.SkipReason = "Downsampled";
                _stats.SkippedValueCount++;
                _stats.DownsampledValueCount++;
            }
            else if (ShouldCompress(state, current))
            {
                result.CompressionSkipped = true;
                result.SkipReason = "Compressed";
                _stats.SkippedValueCount++;
                _stats.CompressedValueCount++;
            }
            else
            {
                result.WriteCurrent = true;
                result.Current = current;
                state.LastWritten = current.Point.Clone();
                _stats.WrittenValueCount++;
            }

            state.LastReceived = point.Clone();
        }

        return result;
    }

    private EdgeDataPoint NormalizeInput(EdgeDataPoint input)
    {
        EdgeDataPoint point = input.Clone();
        point.TagKey = string.IsNullOrWhiteSpace(point.TagKey) ? "_" : point.TagKey.Trim();
        if (point.Timestamp == DateTime.MinValue)
            point.Timestamp = DateTime.Now;
        point.ValueText = point.ValueText ?? string.Empty;
        point.RawValueText = point.RawValueText ?? string.Empty;
        point.Quality = point.Quality ?? string.Empty;
        point.Unit = point.Unit ?? string.Empty;
        return point;
    }

    private EdgeTagProcessingState GetState(string tagKey)
    {
        if (!_states.TryGetValue(tagKey, out EdgeTagProcessingState? state))
        {
            state = new EdgeTagProcessingState();
            _states[tagKey] = state;
        }

        return state;
    }

    private EdgeProcessedDataPoint CreateCurrent(EdgeDataPoint point)
    {
        EdgeDataPoint stored = point.Clone();
        DateTime originalTimestamp = stored.Timestamp;
        if (_options.Enabled && _options.AlignmentEnabled && _options.AlignmentIntervalMs > 0)
            stored.Timestamp = AlignTimestamp(stored.Timestamp, TimeSpan.FromMilliseconds(_options.AlignmentIntervalMs));

        return new EdgeProcessedDataPoint
        {
            Point = stored,
            ProcessingType = stored.Timestamp == originalTimestamp ? "raw" : "aligned",
            Reason = stored.Timestamp == originalTimestamp ? string.Empty : "Timestamp aligned.",
            OriginalTimestamp = originalTimestamp
        };
    }

    private bool ShouldDownsample(EdgeTagProcessingState state, EdgeProcessedDataPoint current)
    {
        if (!_options.Enabled || !_options.DownsamplingEnabled || _options.DownsamplingIntervalMs <= 0 || state.LastWritten == null)
            return false;

        TimeSpan interval = TimeSpan.FromMilliseconds(_options.DownsamplingIntervalMs);
        return current.Point.Timestamp - state.LastWritten.Timestamp < interval;
    }

    private bool ShouldCompress(EdgeTagProcessingState state, EdgeProcessedDataPoint current)
    {
        if (!_options.Enabled || !_options.CompressionEnabled || state.LastWritten == null)
            return false;

        EdgeDataPoint previous = state.LastWritten;
        if (!string.Equals(previous.Quality, current.Point.Quality, StringComparison.OrdinalIgnoreCase))
            return false;

        if (previous.HasNumericValue && current.Point.HasNumericValue)
            return Math.Abs(previous.NumericValue - current.Point.NumericValue) <= _options.CompressionTolerance;

        return _options.CompressDuplicateText &&
               string.Equals(previous.ValueText, current.Point.ValueText, StringComparison.OrdinalIgnoreCase);
    }

    private void AddFillPoints(EdgeTagProcessingState state, EdgeProcessedDataPoint current, EdgeDataProcessingResult result)
    {
        if (!_options.Enabled || !_options.FillEnabled || state.LastWritten == null)
            return;

        TimeSpan interval = ResolveFillInterval();
        if (interval <= TimeSpan.Zero)
            return;

        EdgeDataPoint previous = state.LastWritten;
        DateTime next = previous.Timestamp.Add(interval);
        if (next >= current.Point.Timestamp)
            return;

        TimeSpan gap = current.Point.Timestamp - previous.Timestamp;
        if (_options.FillMaxGapSeconds > 0 && gap > TimeSpan.FromSeconds(_options.FillMaxGapSeconds))
            return;

        int emitted = 0;
        while (next < current.Point.Timestamp && emitted < _options.MaxSyntheticPointsPerInput)
        {
            EdgeDataPoint filled = CreateFilledPoint(previous, current.Point, next);
            result.DerivedPoints.Add(new EdgeProcessedDataPoint
            {
                Point = filled,
                ProcessingType = "fill",
                Reason = "Missing aligned sample filled.",
                OriginalTimestamp = current.OriginalTimestamp
            });
            _stats.FilledValueCount++;
            _stats.WrittenValueCount++;
            emitted++;
            next = next.Add(interval);
        }
    }

    private TimeSpan ResolveFillInterval()
    {
        if (_options.FillIntervalMs > 0)
            return TimeSpan.FromMilliseconds(_options.FillIntervalMs);
        if (_options.AlignmentEnabled && _options.AlignmentIntervalMs > 0)
            return TimeSpan.FromMilliseconds(_options.AlignmentIntervalMs);
        if (_options.DownsamplingEnabled && _options.DownsamplingIntervalMs > 0)
            return TimeSpan.FromMilliseconds(_options.DownsamplingIntervalMs);
        if (_options.AggregationEnabled && _options.AggregationIntervalSeconds > 0)
            return TimeSpan.FromSeconds(_options.AggregationIntervalSeconds);
        return TimeSpan.Zero;
    }

    private EdgeDataPoint CreateFilledPoint(EdgeDataPoint previous, EdgeDataPoint current, DateTime timestamp)
    {
        EdgeDataPoint filled = previous.Clone();
        filled.Timestamp = timestamp;
        if (_options.FillMode.Equals("Linear", StringComparison.OrdinalIgnoreCase) &&
            previous.HasNumericValue &&
            current.HasNumericValue &&
            current.Timestamp > previous.Timestamp)
        {
            double ratio = (timestamp - previous.Timestamp).TotalMilliseconds / (current.Timestamp - previous.Timestamp).TotalMilliseconds;
            double value = previous.NumericValue + (current.NumericValue - previous.NumericValue) * ratio;
            filled.NumericValue = value;
            filled.HasNumericValue = true;
            filled.ValueText = value.ToString("R", CultureInfo.InvariantCulture);
        }

        return filled;
    }

    private void AddAggregatePoints(EdgeTagProcessingState state, EdgeDataPoint point, EdgeDataProcessingResult result)
    {
        if (!_options.Enabled || !_options.AggregationEnabled || _options.AggregationIntervalSeconds <= 0 || !point.HasNumericValue)
            return;

        TimeSpan interval = TimeSpan.FromSeconds(_options.AggregationIntervalSeconds);
        DateTime bucketStart = AlignTimestamp(point.Timestamp, interval);
        if (state.Aggregate == null)
        {
            state.Aggregate = new AggregateWindow(bucketStart, bucketStart.Add(interval), point);
        }
        else if (point.Timestamp >= state.Aggregate.End)
        {
            FlushAggregate(state.Aggregate, result);
            state.Aggregate = new AggregateWindow(bucketStart, bucketStart.Add(interval), point);
        }

        state.Aggregate.Add(point);
    }

    private void FlushAggregate(AggregateWindow window, EdgeDataProcessingResult result)
    {
        foreach (string method in ParseAggregationMethods(_options.AggregationMethods))
        {
            if (!window.TryGetValue(method, out double value))
                continue;

            EdgeDataPoint point = window.Template.Clone();
            point.Timestamp = window.End;
            point.HasNumericValue = true;
            point.NumericValue = value;
            point.ValueText = value.ToString("R", CultureInfo.InvariantCulture);
            result.DerivedPoints.Add(new EdgeProcessedDataPoint
            {
                Point = point,
                ProcessingType = "aggregate",
                Reason = "Window aggregate generated.",
                AggregateMethod = method,
                SampleCount = window.Count,
                WindowStart = window.Start,
                WindowEnd = window.End
            });
            _stats.AggregatedValueCount++;
            _stats.WrittenValueCount++;
        }
    }

    private static IEnumerable<string> ParseAggregationMethods(string methods)
    {
        string[] parts = (methods ?? string.Empty).Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string method = part.Trim();
            if (method.Equals("Average", StringComparison.OrdinalIgnoreCase)) yield return "Average";
            else if (method.Equals("Min", StringComparison.OrdinalIgnoreCase)) yield return "Min";
            else if (method.Equals("Max", StringComparison.OrdinalIgnoreCase)) yield return "Max";
            else if (method.Equals("Sum", StringComparison.OrdinalIgnoreCase)) yield return "Sum";
            else if (method.Equals("Count", StringComparison.OrdinalIgnoreCase)) yield return "Count";
            else if (method.Equals("First", StringComparison.OrdinalIgnoreCase)) yield return "First";
            else if (method.Equals("Last", StringComparison.OrdinalIgnoreCase)) yield return "Last";
        }
    }

    private static DateTime AlignTimestamp(DateTime timestamp, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            return timestamp;
        long ticks = timestamp.Ticks - timestamp.Ticks % interval.Ticks;
        return new DateTime(ticks, timestamp.Kind);
    }

    private sealed class EdgeTagProcessingState
    {
        public EdgeDataPoint? LastReceived { get; set; }
        public EdgeDataPoint? LastWritten { get; set; }
        public AggregateWindow? Aggregate { get; set; }
    }

    private sealed class AggregateWindow
    {
        private double _sum;
        private double _min;
        private double _max;
        private double _first;
        private double _last;

        public AggregateWindow(DateTime start, DateTime end, EdgeDataPoint template)
        {
            Start = start;
            End = end;
            Template = template.Clone();
        }

        public DateTime Start { get; }
        public DateTime End { get; }
        public EdgeDataPoint Template { get; }
        public int Count { get; private set; }

        public void Add(EdgeDataPoint point)
        {
            double value = point.NumericValue;
            if (Count == 0)
            {
                _min = value;
                _max = value;
                _first = value;
            }

            _sum += value;
            _last = value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;
            Count++;
        }

        public bool TryGetValue(string method, out double value)
        {
            value = 0D;
            if (Count <= 0)
                return false;

            if (method.Equals("Average", StringComparison.OrdinalIgnoreCase)) value = _sum / Count;
            else if (method.Equals("Min", StringComparison.OrdinalIgnoreCase)) value = _min;
            else if (method.Equals("Max", StringComparison.OrdinalIgnoreCase)) value = _max;
            else if (method.Equals("Sum", StringComparison.OrdinalIgnoreCase)) value = _sum;
            else if (method.Equals("Count", StringComparison.OrdinalIgnoreCase)) value = Count;
            else if (method.Equals("First", StringComparison.OrdinalIgnoreCase)) value = _first;
            else if (method.Equals("Last", StringComparison.OrdinalIgnoreCase)) value = _last;
            else return false;
            return true;
        }
    }
}
