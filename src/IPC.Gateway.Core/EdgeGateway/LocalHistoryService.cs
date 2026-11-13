/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：LocalHistoryService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IPC.Gateway.DataProcessing;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    
    public sealed class LocalHistoryService : IDisposable
    {
        private readonly object _syncRoot;
        private static readonly System.Text.Json.JsonSerializerOptions ValueLineJson = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private CancellationTokenSource _maintenanceCancellation = new();
        private Task _maintenanceTask = Task.CompletedTask;
        private bool _maintenanceActive;
        private bool _maintenanceRequested;
        private readonly IRuntimeService _runtime;
        private readonly LocalHistoryOptions _options;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly EdgeDataProcessor _dataProcessor;
        private readonly TagValueChangedWorker _tagValueWorker;
        private readonly object _writeQueueSyncRoot;
        private readonly Queue<LocalHistoryWriteItem> _writeQueue;
        private Thread? _writerThread;
        private DateTime _lastCleanupTime;
        private DateTime _lastErrorTime;
        private string _lastError;
        private bool _running;
        private bool _writerStopping;
        private bool _disposed;
        public HistoryValueIndex ValueQueries { get; }

        public LocalHistoryService(IRuntimeService runtime, LocalHistoryOptions options)
            : this(runtime, options, new GatewayResilienceOptions().History)
        {
        }

        public LocalHistoryService(IRuntimeService runtime, LocalHistoryOptions options, CircuitBreakerOptions circuitBreakerOptions)
        {
            _runtime = runtime;
            _options = options == null ? new LocalHistoryOptions() : options.Clone();
            ValueQueries = new HistoryValueIndex(ResolveDirectory(), ResolveColdDirectory());
            _syncRoot = new object();
            _writeQueueSyncRoot = new object();
            _writeQueue = new Queue<LocalHistoryWriteItem>();
            _circuitBreaker = new CircuitBreaker("History", circuitBreakerOptions ?? new GatewayResilienceOptions().History);
            _dataProcessor = new EdgeDataProcessor(_options.DataProcessing);
            _tagValueWorker = new TagValueChangedWorker(
                "IPC Local History Tag Worker",
                Math.Max(1, _options.BatchQueueLimit),
                ProcessTagValueChanged,
                ex => IpcLogService.WriteError("Local history tag queue failed.", ex));
            _lastCleanupTime = DateTime.MinValue;
            _lastErrorTime = DateTime.MinValue;
            _lastError = string.Empty;
        }

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                    return _running;
            }
        }

        public void Start()
        {
            // Previously recorded samples stay searchable when recording is switched off.
            ValueQueries.Start();
            if (!_options.Enabled || _runtime == null)
                return;

            lock (_syncRoot)
            {
                if (_running)
                    return;
                if (_maintenanceCancellation.IsCancellationRequested)
                {
                    _maintenanceCancellation.Dispose();
                    _maintenanceCancellation = new();
                }
                _running = true;
            }

            StartWriter();
            _tagValueWorker.Start();
            _runtime.TagValueChanged -= OnTagValueChanged;
            _runtime.TagValueChanged += OnTagValueChanged;
            Cleanup();
        }

        public void Stop()
        {
            if (_runtime != null)
                _runtime.TagValueChanged -= OnTagValueChanged;

            Task maintenance;
            lock (_syncRoot)
            {
                _running = false;
                if (!_maintenanceCancellation.IsCancellationRequested) _maintenanceCancellation.Cancel();
                maintenance = _maintenanceTask;
            }

            _tagValueWorker.Stop(TimeSpan.FromSeconds(3));
            StopWriter();
            maintenance.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ValueQueries.Dispose();
            Stop();
            _maintenanceCancellation.Dispose();
        }

        public LocalHistoryStats GetStats()
        {
            lock (_syncRoot)
            {
                string directory = ResolveDirectory();
                string coldDirectory = ResolveColdDirectory();
                LocalHistoryStorageOptions storage = LocalHistoryStorageOptions.Normalize(_options.Storage, _options.RetentionDays);
                LocalHistoryStats stats = new LocalHistoryStats
                {
                    Enabled = _options.Enabled,
                    IsRunning = _running,
                    Directory = directory,
                    RetentionDays = LocalHistoryOptions.ClampRetentionDays(_options.RetentionDays),
                    ColdDirectory = coldDirectory,
                    TieringEnabled = storage.TieringEnabled,
                    RetentionPolicy = storage.RetentionPolicy,
                    HotRetentionDays = storage.HotRetentionDays,
                    ColdRetentionDays = storage.ColdRetentionDays,
                    StorageCompressionEnabled = storage.CompressionEnabled,
                    AutoCleanupEnabled = storage.AutoCleanupEnabled,
                    CleanupIntervalHours = storage.CleanupIntervalHours,
                    LastCleanupTime = _lastCleanupTime,
                    NextCleanupTime = _lastCleanupTime == DateTime.MinValue ? DateTime.MinValue : _lastCleanupTime.AddHours(storage.CleanupIntervalHours),
                    DataProcessingEnabled = _dataProcessor.Options.Enabled,
                    CompressionEnabled = _dataProcessor.Options.CompressionEnabled,
                    DownsamplingEnabled = _dataProcessor.Options.DownsamplingEnabled,
                    AlignmentEnabled = _dataProcessor.Options.AlignmentEnabled,
                    FillEnabled = _dataProcessor.Options.FillEnabled,
                    AggregationEnabled = _dataProcessor.Options.AggregationEnabled
                };
                ApplyDataProcessingStats(stats);
                ApplyCircuitBreakerStats(stats);

                ApplyDirectoryStats(stats, directory, true);
                if (!PathEquals(directory, coldDirectory))
                    ApplyDirectoryStats(stats, coldDirectory, false);

                return stats;
            }
        }

        public IList<LocalHistoryEntry> ReadRecent(string category, int maxRecords)
        {
            List<LocalHistoryEntry> entries = new();
            if (!_circuitBreaker.CanExecute())
                return entries;

            string prefix = NormalizeCategory(category) + "-";
            string hot = ResolveDirectory();
            string cold = ResolveColdDirectory();
            int limit = LocalHistoryOptions.ClampMaxViewRecords(maxRecords);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                entries.Clear();
                try
                {
                    List<string> files = new();
                    lock (_syncRoot)
                    {
                        AddHistoryFiles(hot, prefix, files);
                        if (!PathEquals(hot, cold))
                            AddHistoryFiles(cold, prefix, files);
                    }
                    files.Sort(CompareHistoryFilesDescending);
                    foreach (string path in files)
                    {
                        if (entries.Count >= limit)
                            break;
                        try
                        {
                            ReadFileRecent(path, entries, limit);
                            lock (_syncRoot) RecordHistorySuccess();
                        }
                        catch (Exception ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException))
                        {
                            lock (_syncRoot) RecordHistoryFailure(ex);
                        }
                    }
                    break;
                }
                catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Rotation, retention and hot-to-cold moves can change the listing.
                    continue;
                }
                catch (Exception ex)
                {
                    lock (_syncRoot) RecordHistoryFailure(ex);
                    break;
                }
            }
            return entries;
        }

        public void RecordPublish(string source, string topic, int qos, string payload)
        {
            if (!_options.Enabled)
                return;

            string line = "{" +
                          "\"timestamp\":\"" + JsonEscape(DateTime.Now.ToString("o")) + "\"," +
                          "\"type\":\"publish\"," +
                          "\"source\":\"" + JsonEscape(source) + "\"," +
                          "\"topic\":\"" + JsonEscape(topic) + "\"," +
                          "\"qos\":" + qos.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"payload\":\"" + JsonEscape(TrimPayload(payload)) + "\"" +
                          "}";
            Append("publishes", line);
        }

        public void RecordAlarm(TagValueSnapshot snapshot, string eventType, string state, string message, double value, double threshold, string topic)
        {
            if (!_options.Enabled || snapshot == null)
                return;

            string line = "{" +
                          "\"timestamp\":\"" + JsonEscape(DateTime.Now.ToString("o")) + "\"," +
                          "\"type\":\"" + JsonEscape(eventType) + "\"," +
                          "\"state\":\"" + JsonEscape(state) + "\"," +
                          "\"message\":\"" + JsonEscape(message) + "\"," +
                          "\"value\":" + value.ToString("R", CultureInfo.InvariantCulture) + "," +
                          "\"threshold\":" + threshold.ToString("R", CultureInfo.InvariantCulture) + "," +
                          "\"topic\":\"" + JsonEscape(topic) + "\"," +
                          BuildSnapshotFields(snapshot) +
                          "}";
            Append("alarms", line);
        }

        private void OnTagValueChanged(object? sender, TagValueChangedEventArgs e)
        {
            if (!_options.Enabled || e == null || e.Snapshot == null)
                return;

            _tagValueWorker.Enqueue(e.Snapshot);
        }

        private void ProcessTagValueChanged(TagValueSnapshot snapshot)
        {
            if (!_options.Enabled || snapshot == null)
                return;

            if (!_dataProcessor.Options.Enabled)
            {
                Append("values", BuildValueLine(snapshot, null));
                return;
            }

            EdgeDataProcessingResult result = _dataProcessor.Process(ToEdgeDataPoint(snapshot));
            for (int i = 0; i < result.DerivedPoints.Count; i++)
                Append("values", BuildValueLine(snapshot, result.DerivedPoints[i]));

            if (result.WriteCurrent && result.Current != null)
                Append("values", BuildValueLine(snapshot, result.Current));
        }

        private static string BuildValueLine(TagValueSnapshot snapshot, EdgeProcessedDataPoint? processed)
        {
            DateTime timestamp = processed == null ? snapshot.Timestamp : processed.Point.Timestamp;
            string valueText = (processed == null ? snapshot.ValueText : processed.Point.ValueText) ?? string.Empty;
            string rawValueText = (processed == null ? snapshot.RawValueText : processed.Point.RawValueText) ?? string.Empty;
            string quality = processed == null ? snapshot.Quality.ToString() : processed.Point.Quality;
            string unit = processed == null ? snapshot.Unit : processed.Point.Unit;
            string processingType = processed == null ? "raw" : processed.ProcessingType;
            string processingReason = processed == null ? string.Empty : processed.Reason;
            DateTime originalTimestamp = processed == null || processed.OriginalTimestamp == DateTime.MinValue
                ? snapshot.Timestamp : processed.OriginalTimestamp;
            string aggregateMethod = processed == null ? string.Empty : processed.AggregateMethod;
            string windowStart = processed == null || processed.WindowStart == DateTime.MinValue ? string.Empty : processed.WindowStart.ToString("o");
            string windowEnd = processed == null || processed.WindowEnd == DateTime.MinValue ? string.Empty : processed.WindowEnd.ToString("o");
            int sampleCount = processed == null ? 0 : processed.SampleCount;

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = timestamp.ToString("o"),
                type = "value",
                valueText,
                rawValueText = rawValueText == valueText ? null : rawValueText,
                quality,
                channelId = snapshot.ChannelId,
                channel = snapshot.ChannelName,
                deviceId = snapshot.DeviceId,
                device = snapshot.DeviceName,
                groupId = EmptyToNull(snapshot.GroupId),
                group = EmptyToNull(snapshot.GroupName),
                tagId = snapshot.TagId,
                tag = snapshot.TagName,
                pointCode = GetPointCode(snapshot),
                dataType = snapshot.DataType,
                unit = EmptyToNull(unit),
                assetPath = EmptyToNull(snapshot.AssetPath),
                businessType = EmptyToNull(snapshot.BusinessType),
                source = EmptyToNull(snapshot.Source),
                errorMessage = EmptyToNull(snapshot.ErrorMessage),
                cleaningApplied = snapshot.CleaningApplied ? (bool?)true : null,
                cleaningAction = EmptyToNull(snapshot.CleaningAction),
                cleaningMessage = EmptyToNull(snapshot.CleaningMessage),
                edgeProcessingType = processingType == "raw" ? null : processingType,
                edgeProcessingReason = EmptyToNull(processingReason),
                edgeOriginalTimestamp = originalTimestamp == timestamp && originalTimestamp.Kind == timestamp.Kind
                    ? null : originalTimestamp.ToString("o"),
                edgeAggregateMethod = EmptyToNull(aggregateMethod),
                edgeSampleCount = sampleCount == 0 ? (int?)null : sampleCount,
                edgeWindowStart = EmptyToNull(windowStart),
                edgeWindowEnd = EmptyToNull(windowEnd)
            }, ValueLineJson);
        }

        private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static EdgeDataPoint ToEdgeDataPoint(TagValueSnapshot snapshot)
        {
            EdgeDataPoint point = new EdgeDataPoint
            {
                TagKey = GetPointCode(snapshot),
                Timestamp = snapshot.Timestamp,
                ValueText = snapshot.ValueText ?? string.Empty,
                RawValueText = snapshot.RawValueText ?? string.Empty,
                Quality = snapshot.Quality.ToString(),
                Unit = snapshot.Unit ?? string.Empty
            };

            double value;
            if (TryToDouble(snapshot.Value, out value) || TryToDouble(snapshot.ValueText, out value))
            {
                point.HasNumericValue = true;
                point.NumericValue = value;
            }

            return point;
        }

        private void Append(string category, string line)
        {
            if (!_circuitBreaker.CanExecute())
            {
                RecordHistoryDegraded("History circuit breaker is open; write skipped.");
                return;
            }

            if (_options.BatchWriteEnabled)
            {
                EnqueueWrite(category, line);
                return;
            }

            AppendImmediate(category, line);
        }

        private void AppendImmediate(string category, string line)
        {
            lock (_syncRoot)
            {
                try
                {
                    ScheduleMaintenance();
                    string directory = ResolveDirectory();
                    System.IO.Directory.CreateDirectory(directory);
                    AppendCategory(directory, NormalizeCategory(category), line + Environment.NewLine);
                    RecordHistorySuccess();
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                    IpcLogService.WriteError("Local history append failed.", ex);
                }
            }
        }

        private void EnqueueWrite(string category, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            bool writeImmediate = false;
            bool dropped = false;
            lock (_writeQueueSyncRoot)
            {
                if (_writerStopping || _writerThread == null)
                {
                    writeImmediate = true;
                }
                else
                {
                    int queueLimit = Math.Max(1, _options.BatchQueueLimit);
                    if (_writeQueue.Count >= queueLimit)
                    {
                        _writeQueue.Dequeue();
                        dropped = true;
                    }

                    _writeQueue.Enqueue(new LocalHistoryWriteItem(category, line));
                    Monitor.Pulse(_writeQueueSyncRoot);
                }
            }

            if (writeImmediate)
                AppendImmediate(category, line);
            if (dropped)
                RecordHistoryDegraded("History write queue is full; oldest record was dropped.");
        }

        private void StartWriter()
        {
            if (!_options.BatchWriteEnabled)
                return;

            lock (_writeQueueSyncRoot)
            {
                if (_writerThread != null)
                    return;

                _writerStopping = false;
                _writerThread = new Thread(ProcessWriteQueue);
                _writerThread.IsBackground = true;
                _writerThread.Name = "IPC Local History Writer";
                _writerThread.Start();
            }
        }

        private void StopWriter()
        {
            Thread? thread;
            lock (_writeQueueSyncRoot)
            {
                _writerStopping = true;
                Monitor.PulseAll(_writeQueueSyncRoot);
                thread = _writerThread;
            }

            if (thread != null && thread.IsAlive)
                thread.Join(3000);

            FlushWriteQueue();

            lock (_writeQueueSyncRoot)
            {
                if (ReferenceEquals(_writerThread, thread))
                    _writerThread = null;
            }
        }

        private void ProcessWriteQueue()
        {
            while (true)
            {
                List<LocalHistoryWriteItem> batch;
                lock (_writeQueueSyncRoot)
                {
                    if (!_writerStopping && _writeQueue.Count == 0)
                        Monitor.Wait(_writeQueueSyncRoot, Math.Max(100, _options.BatchFlushIntervalMs));

                    if (_writerStopping && _writeQueue.Count == 0)
                        return;

                    batch = DequeueWriteBatchNoLock();
                }

                WriteBatch(batch);
            }
        }

        private void FlushWriteQueue()
        {
            while (true)
            {
                List<LocalHistoryWriteItem> batch;
                lock (_writeQueueSyncRoot)
                {
                    if (_writeQueue.Count == 0)
                        return;

                    batch = DequeueWriteBatchNoLock();
                }

                WriteBatch(batch);
            }
        }

        private List<LocalHistoryWriteItem> DequeueWriteBatchNoLock()
        {
            int count = Math.Min(_writeQueue.Count, Math.Max(1, _options.BatchMaxLines));
            List<LocalHistoryWriteItem> batch = new List<LocalHistoryWriteItem>(count);
            for (int i = 0; i < count; i++)
                batch.Add(_writeQueue.Dequeue());
            return batch;
        }

        private void WriteBatch(IList<LocalHistoryWriteItem> batch)
        {
            if (batch == null || batch.Count == 0)
                return;

            lock (_syncRoot)
            {
                try
                {
                    ScheduleMaintenance();
                    string directory = ResolveDirectory();
                    System.IO.Directory.CreateDirectory(directory);
                    Dictionary<string, StringBuilder> linesByCategory = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < batch.Count; i++)
                    {
                        LocalHistoryWriteItem item = batch[i];
                        string category = NormalizeCategory(item.Category);
                        StringBuilder? builder;
                        if (!linesByCategory.TryGetValue(category, out builder))
                        {
                            builder = new StringBuilder();
                            linesByCategory[category] = builder;
                        }

                        builder.AppendLine(item.Line);
                    }

                    foreach (KeyValuePair<string, StringBuilder> pair in linesByCategory)
                    {
                        AppendCategory(directory, pair.Key, pair.Value.ToString());
                    }

                    RecordHistorySuccess();
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                    IpcLogService.WriteError("Local history batch append failed.", ex);
                }
            }
        }

        private void Cleanup()
        {
            lock (_syncRoot)
                ScheduleMaintenance();
        }

        private void AppendCategory(string directory, string category, string lines)
        {
            DateTime now = DateTime.Now;
            string name = category + "-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, name + ".jsonl");
            long segmentBytes = _options.Storage.ValueFileSegmentMegabytes * 1024L * 1024L;
            if (category == "values" && File.Exists(path) && new FileInfo(path).Length >= segmentBytes)
            {
                string suffix = now.ToString("HHmmssfffffff", CultureInfo.InvariantCulture);
                string stem = Path.Combine(directory, name + "-" + suffix);
                string segment = stem + ".jsonl";
                for (int i = 1; File.Exists(segment) || File.Exists(segment + ".gz"); i++)
                    segment = stem + "-" + i.ToString("D4", CultureInfo.InvariantCulture) + ".jsonl";
                File.Move(path, segment);
                ScheduleMaintenance(sealedSegment: true);
            }
            AppendTextAsync(path, lines).GetAwaiter().GetResult();
        }

        // Called under _syncRoot. Maintenance never holds this lock while compressing.
        private void ScheduleMaintenance(bool sealedSegment = false)
        {
            LocalHistoryStorageOptions storage = _options.Storage;
            if (!storage.AutoCleanupEnabled || _maintenanceCancellation.IsCancellationRequested ||
                (!sealedSegment && DateTime.Now - _lastCleanupTime < TimeSpan.FromHours(storage.CleanupIntervalHours)))
                return;
            _lastCleanupTime = DateTime.Now;
            _maintenanceRequested = true;
            if (_maintenanceActive) return;
            _maintenanceActive = true;
            var token = _maintenanceCancellation.Token;
            _maintenanceTask = Task.Run(() => RunMaintenance(storage, token));
        }

        private void RunMaintenance(LocalHistoryStorageOptions storage, CancellationToken token)
        {
            while (true)
            {
                lock (_syncRoot)
                {
                    if (!_maintenanceRequested || token.IsCancellationRequested)
                    {
                        _maintenanceActive = false;
                        return;
                    }
                    _maintenanceRequested = false;
                }
                try { CleanupCore(storage, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) { lock (_syncRoot) RecordHistoryDegraded(ex.Message); }
            }
        }

        private void CleanupCore(LocalHistoryStorageOptions storage, CancellationToken token)
        {
            string hotDirectory = ResolveDirectory();
            string coldDirectory = ResolveColdDirectory();
            DateTime today = DateTime.Today;
            DateTime hotThreshold = today.AddDays(-storage.HotRetentionDays);
            DateTime coldThreshold = today.AddDays(-storage.ColdRetentionDays);
            DateTime compressionThreshold = today.AddDays(-storage.CompressAfterDays);

            CleanupHotDirectory(hotDirectory, coldDirectory, storage, hotThreshold, compressionThreshold, token);
            if (!PathEquals(hotDirectory, coldDirectory))
                CleanupColdDirectory(coldDirectory, storage, coldThreshold, compressionThreshold, token);
            token.ThrowIfCancellationRequested();
            EnforceStorageLimit(hotDirectory, coldDirectory, storage);
        }

        private void CleanupHotDirectory(string hotDirectory, string coldDirectory, LocalHistoryStorageOptions storage, DateTime hotThreshold, DateTime compressionThreshold, CancellationToken token)
        {
            List<string> files = GetHistoryFiles(hotDirectory);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                token.ThrowIfCancellationRequested();
                try
                {
                    if (IsActiveDailyFile(path)) continue;
                    DateTime fileDate = GetHistoryFileDate(path);
                    if (storage.TieringEnabled &&
                        string.Equals(storage.RetentionPolicy, "MoveToColdThenDelete", StringComparison.OrdinalIgnoreCase) &&
                        fileDate < hotThreshold)
                    {
                        MoveToCold(path, coldDirectory, token);
                        continue;
                    }

                    if (string.Equals(storage.RetentionPolicy, "DeleteOnly", StringComparison.OrdinalIgnoreCase) &&
                        fileDate < hotThreshold)
                    {
                        lock (_syncRoot) File.Delete(path);
                        continue;
                    }

                    if (storage.CompressionEnabled && storage.CompressHotFiles && fileDate <= compressionThreshold)
                        CompressFile(path, token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    lock (_syncRoot) RecordHistoryDegraded(ex.Message);
                }
            }
        }

        private void CleanupColdDirectory(string coldDirectory, LocalHistoryStorageOptions storage, DateTime coldThreshold, DateTime compressionThreshold, CancellationToken token)
        {
            List<string> files = GetHistoryFiles(coldDirectory);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                token.ThrowIfCancellationRequested();
                try
                {
                    DateTime fileDate = GetHistoryFileDate(path);
                    if (fileDate < coldThreshold)
                    {
                        lock (_syncRoot) File.Delete(path);
                        continue;
                    }

                    if (storage.CompressionEnabled && storage.CompressColdFiles && fileDate <= compressionThreshold)
                        CompressFile(path, token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    lock (_syncRoot) RecordHistoryDegraded(ex.Message);
                }
            }
        }

        private static bool IsActiveDailyFile(string path)
        {
            string name = Path.GetFileName(path);
            string day = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return name == "values-" + day + ".jsonl" || name == "publishes-" + day + ".jsonl" ||
                name == "alarms-" + day + ".jsonl";
        }

        private void MoveToCold(string path, string coldDirectory, CancellationToken token)
        {
            Directory.CreateDirectory(coldDirectory);
            string target = Path.Combine(coldDirectory, Path.GetFileName(path));
            if (PathEquals(path, target)) return;
            if (RemoveArchivedDuplicate(path, target, compressed: false, token)) return;

            if (string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
            {
                lock (_syncRoot) File.Move(path, target);
            }
            else
                ArchiveFile(path, target, compress: false, token);
        }

        private void CompressFile(string path, CancellationToken token)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return;
            ArchiveFile(path, path + ".gz", compress: true, token);
        }

        private void ArchiveFile(string path, string target, bool compress, CancellationToken token)
        {
            // Never discard the original just because a same-name archive already exists.
            if (RemoveArchivedDuplicate(path, target, compress, token)) return;
            string temporary = target + ".inprogress";
            using (FileStream source = File.OpenRead(path))
            {
                using FileStream destination = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                try
                {
                    if (compress)
                    {
                        using (var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true))
                            source.CopyToAsync(gzip, 81920, token).GetAwaiter().GetResult();
                    }
                    else
                        source.CopyToAsync(destination, 81920, token).GetAwaiter().GetResult();
                    destination.Flush(flushToDisk: true);
                }
                catch
                {
                    destination.Dispose();
                    File.Delete(temporary);
                    throw;
                }
            }
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_syncRoot)
                {
                    File.Move(temporary, target);
                    File.Delete(path);
                }
            }
            finally { File.Delete(temporary); }
        }

        private bool RemoveArchivedDuplicate(string path, string target, bool compressed, CancellationToken token)
        {
            if (!File.Exists(target)) return false;
            // A process may have stopped after publishing the archive and before unlinking the source.
            // Old installations may instead have left a truncated archive under the final name.
            using (Stream source = File.OpenRead(path))
            using (Stream archivedFile = File.OpenRead(target))
            using (Stream archived = compressed ? new GZipStream(archivedFile, CompressionMode.Decompress) : archivedFile)
            {
                byte[] expected = new byte[81920], actual = new byte[expected.Length];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int count = source.Read(expected);
                    if (count == 0 && archived.ReadByte() == -1) break;
                    if (count == 0 || archived.ReadAtLeast(actual.AsSpan(0, count), count, throwOnEndOfStream: false) != count ||
                        !expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)))
                        throw new IOException("历史归档内容不一致，已保留两个文件：" + target);
                }
            }
            lock (_syncRoot) File.Delete(path);
            return true;
        }

        private void EnforceStorageLimit(string hotDirectory, string coldDirectory, LocalHistoryStorageOptions storage)
        {
            if (storage.MaxStorageMegabytes <= 0)
                return;

            long limitBytes = storage.MaxStorageMegabytes * 1024L * 1024L;
            List<string> files = new List<string>();
            AddHistoryFiles(coldDirectory, string.Empty, files);
            if (!PathEquals(hotDirectory, coldDirectory))
                AddHistoryFiles(hotDirectory, string.Empty, files);

            long totalBytes = 0;
            for (int i = 0; i < files.Count; i++)
                totalBytes += GetFileSize(files[i]);
            if (totalBytes <= limitBytes)
                return;

            files.Sort(CompareHistoryFilesAscending);
            for (int i = 0; i < files.Count && totalBytes > limitBytes; i++)
            {
                if (IsActiveDailyFile(files[i]) && PathEquals(Path.GetDirectoryName(files[i])!, hotDirectory))
                    continue;
                try
                {
                    long size = GetFileSize(files[i]);
                    lock (_syncRoot) File.Delete(files[i]);
                    totalBytes -= size;
                }
                catch (Exception ex)
                {
                    lock (_syncRoot) RecordHistoryDegraded(ex.Message);
                }
            }
        }

        private static void ReadFileRecent(string path, List<LocalHistoryEntry> entries, int limit)
        {
            int remaining = limit - entries.Count;
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool compressed = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            if (!compressed) SeekToRecentRecords(file, remaining);
            using GZipStream? gzip = compressed ? new(file, CompressionMode.Decompress, leaveOpen: true) : null;
            using StreamReader reader = new((Stream?)gzip ?? file, Encoding.UTF8);
            Queue<string> tail = new();
            while (reader.ReadLine() is string line)
            {
                if (!line.AsSpan().TrimEnd().EndsWith("}", StringComparison.Ordinal))
                    continue;
                if (tail.Count == remaining) tail.Dequeue();
                tail.Enqueue(line);
                if (!compressed && tail.Count == remaining) break;
            }
            string[] lines = tail.ToArray();
            for (int i = lines.Length - 1; i >= 0; i--)
                entries.Add(ParseEntry(lines[i]));
        }

        private static LocalHistoryEntry ParseEntry(string json)
        {
            JsonDocument? parsed;
            try { parsed = JsonDocument.Parse(json); }
            catch (JsonException) { parsed = null; } // Legacy writers could leave unescaped control characters.
            using var document = parsed;
            string Field(string key)
            {
                if (document == null) return ExtractJsonValue(json, key);
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(key, out var field) ? field.ToString() : string.Empty;
            }
            string type = Field("type");
            DateTime timestamp;
            if (!DateTime.TryParse(Field("timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                timestamp = DateTime.MinValue;

            string source = Field("source");
            string topic = Field("topic");
            string pointCode = Field("pointCode");
            string channelId = Field("channelId");
            string channel = Field("channel");
            string deviceId = Field("deviceId");
            string device = Field("device");
            string groupId = Field("groupId");
            string group = Field("group");
            string tagId = Field("tagId");
            string tag = Field("tag");
            string valueText = Field("valueText");
            string message = Field("message");
            string quality = Field("quality");

            string summary;
            if (type == "publish")
                summary = source + " -> " + topic;
            else if (type == "alarm" || type == "warning")
                summary = type + " " + pointCode + " " + message;
            else
                summary = pointCode + " = " + valueText + " " + quality;

            return new LocalHistoryEntry
            {
                Timestamp = timestamp,
                Type = type,
                Source = string.IsNullOrWhiteSpace(pointCode) ? source : pointCode,
                Summary = summary,
                Detail = "channel=" + channel + " device=" + device + " group=" + group + " tag=" + tag + " topic=" + topic,
                ChannelId = channelId,
                ChannelName = channel,
                DeviceId = deviceId,
                DeviceName = device,
                GroupId = groupId,
                GroupName = group,
                TagId = tagId,
                TagName = tag,
                PointCode = pointCode
            };
        }

        private void ApplyDirectoryStats(LocalHistoryStats stats, string directory, bool hot)
        {
            if (!System.IO.Directory.Exists(directory))
                return;

            try
            {
                List<string> files = GetHistoryFiles(directory);
                for (int i = 0; i < files.Count; i++)
                {
                    string name = Path.GetFileName(files[i]) ?? string.Empty;
                    if (name.StartsWith("values-", StringComparison.OrdinalIgnoreCase))
                        stats.ValueFiles++;
                    else if (name.StartsWith("alarms-", StringComparison.OrdinalIgnoreCase))
                        stats.AlarmFiles++;
                    else if (name.StartsWith("publishes-", StringComparison.OrdinalIgnoreCase))
                        stats.PublishFiles++;

                    long size = GetFileSize(files[i]);
                    stats.TotalBytes += size;
                    if (hot)
                    {
                        stats.HotFileCount++;
                        stats.HotBytes += size;
                    }
                    else
                    {
                        stats.ColdFileCount++;
                        stats.ColdBytes += size;
                    }

                    if (files[i].EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.CompressedFileCount++;
                        stats.CompressedBytes += size;
                    }
                }
            }
            catch (Exception ex)
            {
                RecordHistoryFailure(ex);
                ApplyCircuitBreakerStats(stats);
            }
        }

        private static void SeekToRecentRecords(FileStream file, int count)
        {
            // Newlines and the closing JSON brace are single bytes in UTF-8. Seek to a
            // whole line before decoding so that multibyte characters survive block boundaries.
            byte[] block = new byte[32 * 1024];
            long offset = file.Length;
            byte lastNonWhitespace = 0;
            while (offset > 0)
            {
                int length = (int)Math.Min(offset, block.Length);
                offset -= length;
                file.Position = offset;
                file.ReadExactly(block.AsSpan(0, length));
                for (int i = length - 1; i >= 0; i--)
                {
                    byte b = block[i];
                    if (b == '\n')
                    {
                        if (lastNonWhitespace == '}' && --count == 0)
                        {
                            file.Position = offset + i + 1;
                            return;
                        }
                        lastNonWhitespace = 0;
                    }
                    else if (lastNonWhitespace == 0 && b != '\r' && b != '\t' && b != ' ')
                        lastNonWhitespace = b;
                }
            }
            file.Position = 0;
        }

        private static async Task AppendTextAsync(string path, string text)
        {
            UTF8Encoding encoding = new UTF8Encoding(false);
            byte[] bytes = encoding.GetBytes(text ?? string.Empty);
            using FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static List<string> GetHistoryFiles(string directory)
        {
            List<string> files = new List<string>();
            AddHistoryFiles(directory, string.Empty, files);
            return files;
        }

        private static void AddHistoryFiles(string directory, string prefix, List<string> files)
        {
            if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
                return;

            foreach (string path in Directory.EnumerateFiles(directory, prefix + "*.jsonl*", SearchOption.TopDirectoryOnly))
            {
                if (TryGetHistoryFileDate(path, out _))
                    files.Add(path);
            }
        }

        private static int CompareHistoryFilesDescending(string left, string right)
        {
            return CompareHistoryFilesAscending(right, left);
        }

        private static int CompareHistoryFilesAscending(string left, string right)
        {
            int dateCompare = GetHistoryFileDate(left).CompareTo(GetHistoryFileDate(right));
            if (dateCompare != 0)
                return dateCompare;
            var a = HistorySegmentSuffix(left);
            var b = HistorySegmentSuffix(right);
            if (a.IsEmpty != b.IsEmpty)
                return a.IsEmpty ? 1 : -1; // The active file follows every sealed segment of the day.
            return a.CompareTo(b, StringComparison.Ordinal);
        }

        private static ReadOnlySpan<char> HistorySegmentSuffix(string path)
        {
            var name = Path.GetFileName(path.AsSpan());
            int secondDash = name.IndexOf('-') + 9;
            int extension = name.IndexOf('.');
            return extension > secondDash && name[secondDash] == '-'
                ? name[(secondDash + 1)..extension] : ReadOnlySpan<char>.Empty;
        }

        private static DateTime GetHistoryFileDate(string path)
        {
            TryGetHistoryFileDate(path, out DateTime timestamp);
            return timestamp.Date;
        }

        // Hot and cold directories can contain user files. Manage only names produced by AppendCategory.
        internal static bool TryGetHistoryFileDate(string path, out DateTime timestamp)
        {
            timestamp = default;
            ReadOnlySpan<char> name = Path.GetFileName(path.AsSpan());
            if (name.EndsWith(".gz", StringComparison.Ordinal))
                name = name[..^3];
            if (!name.EndsWith(".jsonl", StringComparison.Ordinal))
                return false;
            name = name[..^6];
            int dash = name.IndexOf('-');
            if (dash < 0 || name[..dash] is not ("values" or "alarms" or "publishes"))
                return false;

            var stamp = name[(dash + 1)..];
            if (stamp.Length == 8)
                return DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
            const int segmentStampLength = 22; // yyyyMMdd-HHmmssfffffff
            if (name[..dash] is not "values" || stamp.Length < segmentStampLength)
                return false;
            if (stamp.Length > segmentStampLength)
            {
                if (stamp[segmentStampLength] != '-') return false;
                var sequence = stamp[(segmentStampLength + 1)..];
                if (sequence.Length < 4 || (sequence.Length > 4 && sequence[0] == '0') ||
                    !int.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number <= 0)
                    return false;
                stamp = stamp[..segmentStampLength];
            }
            return DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
        }

        private static long GetFileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private string ResolveDirectory()
        {
            string path = string.IsNullOrWhiteSpace(_options.Directory) ? "Data\\History" : _options.Directory.Trim();
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return path;
        }

        private string ResolveColdDirectory()
        {
            LocalHistoryStorageOptions storage = LocalHistoryStorageOptions.Normalize(_options.Storage, _options.RetentionDays);
            string path = string.IsNullOrWhiteSpace(storage.ColdDirectory) ? "Data\\HistoryCold" : storage.ColdDirectory.Trim();
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return path;
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCategory(string category)
        {
            if (string.Equals(category, "alarms", StringComparison.OrdinalIgnoreCase))
                return "alarms";
            if (string.Equals(category, "publishes", StringComparison.OrdinalIgnoreCase))
                return "publishes";
            return "values";
        }

        private static string BuildSnapshotFields(TagValueSnapshot snapshot)
        {
            return BuildSnapshotFields(snapshot, snapshot == null ? string.Empty : snapshot.Unit);
        }

        private static string BuildSnapshotFields(TagValueSnapshot snapshot, string unit)
        {
            return "\"channelId\":\"" + JsonEscape(snapshot.ChannelId) + "\"," +
                   "\"channel\":\"" + JsonEscape(snapshot.ChannelName) + "\"," +
                   "\"deviceId\":\"" + JsonEscape(snapshot.DeviceId) + "\"," +
                   "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"groupId\":\"" + JsonEscape(snapshot.GroupId) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
                   "\"tagId\":\"" + JsonEscape(snapshot.TagId) + "\"," +
                   "\"tag\":\"" + JsonEscape(snapshot.TagName) + "\"," +
                   "\"pointCode\":\"" + JsonEscape(GetPointCode(snapshot)) + "\"," +
                   "\"assetPath\":\"" + JsonEscape(snapshot.AssetPath) + "\"," +
                   "\"businessType\":\"" + JsonEscape(snapshot.BusinessType) + "\"," +
                   "\"source\":\"" + JsonEscape(snapshot.Source) + "\"," +
                   "\"unit\":\"" + JsonEscape(unit) + "\"," +
                   "\"dataType\":\"" + JsonEscape(snapshot.DataType) + "\"";
        }

        private static string GetPointCode(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(snapshot.PointCode))
                return snapshot.PointCode.Trim();

            string group = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
            return (NullToEmpty(snapshot.ChannelName).Trim() + "." +
                    NullToEmpty(snapshot.DeviceName).Trim() + "." + group + "." +
                    NullToEmpty(snapshot.TagName).Trim()).Trim('.');
        }

        private static string TrimPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return string.Empty;
            return payload.Length <= 2000 ? payload : payload.Substring(0, 2000);
        }

        private void RecordHistorySuccess()
        {
            _circuitBreaker.RecordSuccess();
            _lastError = string.Empty;
        }

        private void RecordHistoryFailure(Exception ex)
        {
            RecordHistoryFailure(ex == null ? string.Empty : ex.Message);
        }

        private void RecordHistoryFailure(string message)
        {
            _lastErrorTime = DateTime.Now;
            _lastError = message ?? string.Empty;
            _circuitBreaker.RecordFailure(_lastError);
        }

        private void RecordHistoryDegraded(string message)
        {
            _lastErrorTime = DateTime.Now;
            _lastError = message ?? string.Empty;
        }

        private void ApplyCircuitBreakerStats(LocalHistoryStats stats)
        {
            if (stats == null)
                return;

            stats.LastErrorTime = _lastErrorTime;
            stats.LastError = _lastError ?? string.Empty;
            stats.CircuitBreaker = _circuitBreaker.Snapshot();
            stats.IsDegraded = stats.CircuitBreaker.IsOpen || stats.CircuitBreaker.IsHalfOpen;
        }

        private void ApplyDataProcessingStats(LocalHistoryStats stats)
        {
            if (stats == null)
                return;

            EdgeDataProcessingStats processing = _dataProcessor.GetStats();
            stats.ReceivedValueCount = processing.ReceivedValueCount;
            stats.WrittenValueCount = processing.WrittenValueCount;
            stats.SkippedValueCount = processing.SkippedValueCount;
            stats.CompressedValueCount = processing.CompressedValueCount;
            stats.DownsampledValueCount = processing.DownsampledValueCount;
            stats.FilledValueCount = processing.FilledValueCount;
            stats.AggregatedValueCount = processing.AggregatedValueCount;
        }

        private static bool TryToDouble(object? value, out double number)
        {
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                number = 0D;
                return false;
            }
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static string ExtractJsonValue(string json, string name)
        {
            string quoted = "\"" + name + "\"";
            int nameIndex = json.IndexOf(quoted, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                return string.Empty;

            int colonIndex = json.IndexOf(':', nameIndex + quoted.Length);
            if (colonIndex < 0)
                return string.Empty;

            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            if (valueStart >= json.Length)
                return string.Empty;

            if (json[valueStart] == '"')
            {
                valueStart++;
                StringBuilder builder = new StringBuilder();
                bool escape = false;
                for (int i = valueStart; i < json.Length; i++)
                {
                    char c = json[i];
                    if (escape)
                    {
                        builder.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c == 't' ? '\t' : c);
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '"')
                        break;
                    builder.Append(c);
                }
                return builder.ToString();
            }

            int end = valueStart;
            while (end < json.Length && json[end] != ',' && json[end] != '}')
                end++;
            return json.Substring(valueStart, end - valueStart).Trim();
        }

        private static string NullToEmpty(string value)
        {
            return value ?? string.Empty;
        }

        private sealed class LocalHistoryWriteItem
        {
            public LocalHistoryWriteItem(string category, string line)
            {
                Category = category ?? string.Empty;
                Line = line ?? string.Empty;
            }

            public string Category { get; private set; }
            public string Line { get; private set; }
        }
    }
}
