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
        private readonly IRuntimeService _runtime;
        private readonly LocalHistoryOptions _options;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly EdgeDataProcessor _dataProcessor;
        private DateTime _lastCleanupTime;
        private DateTime _lastErrorTime;
        private string _lastError;
        private bool _running;
        private bool _disposed;

        public LocalHistoryService(IRuntimeService runtime, LocalHistoryOptions options)
            : this(runtime, options, new GatewayResilienceOptions().History)
        {
        }

        public LocalHistoryService(IRuntimeService runtime, LocalHistoryOptions options, CircuitBreakerOptions circuitBreakerOptions)
        {
            _runtime = runtime;
            _options = options == null ? new LocalHistoryOptions() : options.Clone();
            _syncRoot = new object();
            _circuitBreaker = new CircuitBreaker("History", circuitBreakerOptions ?? new GatewayResilienceOptions().History);
            _dataProcessor = new EdgeDataProcessor(_options.DataProcessing);
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
            if (!_options.Enabled || _runtime == null)
                return;

            lock (_syncRoot)
            {
                if (_running)
                    return;
                _running = true;
            }

            _runtime.TagValueChanged -= OnTagValueChanged;
            _runtime.TagValueChanged += OnTagValueChanged;
            Cleanup();
        }

        public void Stop()
        {
            if (_runtime != null)
                _runtime.TagValueChanged -= OnTagValueChanged;

            lock (_syncRoot)
                _running = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
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
            lock (_syncRoot)
            {
                List<LocalHistoryEntry> entries = new List<LocalHistoryEntry>();
                if (!_circuitBreaker.CanExecute())
                    return entries;

                string directory = ResolveDirectory();
                if (!System.IO.Directory.Exists(directory))
                    return entries;

                string prefix = NormalizeCategory(category) + "-";
                List<string> files = new List<string>();
                try
                {
                    AddHistoryFiles(directory, prefix, files);
                    string coldDirectory = ResolveColdDirectory();
                    if (!PathEquals(directory, coldDirectory))
                        AddHistoryFiles(coldDirectory, prefix, files);
                    files.Sort(CompareHistoryFilesDescending);
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                    return entries;
                }

                int limit = LocalHistoryOptions.ClampMaxViewRecords(maxRecords);
                for (int i = 0; i < files.Count && entries.Count < limit; i++)
                    ReadFileRecent(files[i], entries, limit);

                return entries;
            }
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

            TagValueSnapshot snapshot = e.Snapshot;
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
            string valueText = processed == null ? snapshot.ValueText : processed.Point.ValueText;
            string rawValueText = processed == null ? snapshot.RawValueText : processed.Point.RawValueText;
            string quality = processed == null ? snapshot.Quality.ToString() : processed.Point.Quality;
            string unit = processed == null ? snapshot.Unit : processed.Point.Unit;
            string processingType = processed == null ? "raw" : processed.ProcessingType;
            string processingReason = processed == null ? string.Empty : processed.Reason;
            string originalTimestamp = processed == null || processed.OriginalTimestamp == DateTime.MinValue
                ? snapshot.Timestamp.ToString("o")
                : processed.OriginalTimestamp.ToString("o");
            string aggregateMethod = processed == null ? string.Empty : processed.AggregateMethod;
            string windowStart = processed == null || processed.WindowStart == DateTime.MinValue ? string.Empty : processed.WindowStart.ToString("o");
            string windowEnd = processed == null || processed.WindowEnd == DateTime.MinValue ? string.Empty : processed.WindowEnd.ToString("o");
            int sampleCount = processed == null ? 0 : processed.SampleCount;

            return "{" +
                   "\"timestamp\":\"" + JsonEscape(timestamp.ToString("o")) + "\"," +
                   "\"type\":\"value\"," +
                   "\"valueText\":\"" + JsonEscape(valueText) + "\"," +
                   "\"rawValueText\":\"" + JsonEscape(rawValueText) + "\"," +
                   "\"quality\":\"" + JsonEscape(quality) + "\"," +
                   "\"edgeProcessingType\":\"" + JsonEscape(processingType) + "\"," +
                   "\"edgeProcessingReason\":\"" + JsonEscape(processingReason) + "\"," +
                   "\"edgeOriginalTimestamp\":\"" + JsonEscape(originalTimestamp) + "\"," +
                   "\"edgeAggregateMethod\":\"" + JsonEscape(aggregateMethod) + "\"," +
                   "\"edgeSampleCount\":" + sampleCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"edgeWindowStart\":\"" + JsonEscape(windowStart) + "\"," +
                   "\"edgeWindowEnd\":\"" + JsonEscape(windowEnd) + "\"," +
                   "\"cleaningApplied\":" + (snapshot.CleaningApplied ? "true" : "false") + "," +
                   "\"cleaningAction\":\"" + JsonEscape(snapshot.CleaningAction) + "\"," +
                   "\"cleaningMessage\":\"" + JsonEscape(snapshot.CleaningMessage) + "\"," +
                   "\"errorMessage\":\"" + JsonEscape(snapshot.ErrorMessage) + "\"," +
                   BuildSnapshotFields(snapshot, unit) +
                   "}";
        }

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

            lock (_syncRoot)
            {
                try
                {
                    CleanupCore();
                    string directory = ResolveDirectory();
                    System.IO.Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, NormalizeCategory(category) + "-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                    RecordHistorySuccess();
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                    IpcLogService.WriteError("Local history append failed.", ex);
                }
            }
        }

        private void Cleanup()
        {
            lock (_syncRoot)
                CleanupCore();
        }

        private void CleanupCore()
        {
            LocalHistoryStorageOptions storage = LocalHistoryStorageOptions.Normalize(_options.Storage, _options.RetentionDays);
            if (!storage.AutoCleanupEnabled)
                return;

            DateTime now = DateTime.Now;
            if (_lastCleanupTime != DateTime.MinValue &&
                now - _lastCleanupTime < TimeSpan.FromHours(Math.Max(1, storage.CleanupIntervalHours)))
                return;

            _lastCleanupTime = now;
            string hotDirectory = ResolveDirectory();
            string coldDirectory = ResolveColdDirectory();
            DateTime today = DateTime.Today;
            DateTime hotThreshold = today.AddDays(-storage.HotRetentionDays);
            DateTime coldThreshold = today.AddDays(-storage.ColdRetentionDays);
            DateTime compressionThreshold = today.AddDays(-storage.CompressAfterDays);

            CleanupHotDirectory(hotDirectory, coldDirectory, storage, hotThreshold, compressionThreshold);
            if (!PathEquals(hotDirectory, coldDirectory))
                CleanupColdDirectory(coldDirectory, storage, coldThreshold, compressionThreshold);
            EnforceStorageLimit(hotDirectory, coldDirectory, storage);
        }

        private void CleanupHotDirectory(string hotDirectory, string coldDirectory, LocalHistoryStorageOptions storage, DateTime hotThreshold, DateTime compressionThreshold)
        {
            List<string> files = GetHistoryFiles(hotDirectory);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                try
                {
                    DateTime fileDate = GetHistoryFileDate(path);
                    if (storage.TieringEnabled &&
                        string.Equals(storage.RetentionPolicy, "MoveToColdThenDelete", StringComparison.OrdinalIgnoreCase) &&
                        fileDate < hotThreshold)
                    {
                        MoveToCold(path, coldDirectory);
                        continue;
                    }

                    if (string.Equals(storage.RetentionPolicy, "DeleteOnly", StringComparison.OrdinalIgnoreCase) &&
                        fileDate < hotThreshold)
                    {
                        File.Delete(path);
                        continue;
                    }

                    if (storage.CompressionEnabled && storage.CompressHotFiles && fileDate < compressionThreshold)
                        CompressFile(path);
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                }
            }
        }

        private void CleanupColdDirectory(string coldDirectory, LocalHistoryStorageOptions storage, DateTime coldThreshold, DateTime compressionThreshold)
        {
            List<string> files = GetHistoryFiles(coldDirectory);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                try
                {
                    DateTime fileDate = GetHistoryFileDate(path);
                    if (fileDate < coldThreshold)
                    {
                        File.Delete(path);
                        continue;
                    }

                    if (storage.CompressionEnabled && storage.CompressColdFiles && fileDate < compressionThreshold)
                        CompressFile(path);
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                }
            }
        }

        private void MoveToCold(string path, string coldDirectory)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            System.IO.Directory.CreateDirectory(coldDirectory);
            string target = Path.Combine(coldDirectory, Path.GetFileName(path));
            if (PathEquals(path, target))
                return;
            if (File.Exists(target))
            {
                File.Delete(path);
                return;
            }

            File.Move(path, target);
        }

        private void CompressFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path) ||
                path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                return;

            string target = path + ".gz";
            if (File.Exists(target))
            {
                File.Delete(path);
                return;
            }

            using (FileStream source = File.OpenRead(path))
            using (FileStream destination = File.Create(target))
            using (GZipStream gzip = new GZipStream(destination, CompressionLevel.Optimal))
                source.CopyTo(gzip);

            File.Delete(path);
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
                try
                {
                    long size = GetFileSize(files[i]);
                    File.Delete(files[i]);
                    totalBytes -= size;
                }
                catch (Exception ex)
                {
                    RecordHistoryFailure(ex);
                }
            }
        }

        private void ReadFileRecent(string path, List<LocalHistoryEntry> entries, int limit)
        {
            string[] lines;
            try
            {
                lines = ReadAllHistoryLines(path);
                RecordHistorySuccess();
            }
            catch (Exception ex)
            {
                RecordHistoryFailure(ex);
                return;
            }

            for (int i = lines.Length - 1; i >= 0 && entries.Count < limit; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    entries.Add(ParseEntry(lines[i]));
            }
        }

        private static LocalHistoryEntry ParseEntry(string json)
        {
            string type = ExtractJsonValue(json, "type");
            DateTime timestamp;
            if (!DateTime.TryParse(ExtractJsonValue(json, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                timestamp = DateTime.MinValue;

            string source = ExtractJsonValue(json, "source");
            string topic = ExtractJsonValue(json, "topic");
            string pointCode = ExtractJsonValue(json, "pointCode");
            string device = ExtractJsonValue(json, "device");
            string group = ExtractJsonValue(json, "group");
            string tag = ExtractJsonValue(json, "tag");
            string valueText = ExtractJsonValue(json, "valueText");
            string message = ExtractJsonValue(json, "message");
            string quality = ExtractJsonValue(json, "quality");

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
                Detail = "device=" + device + " group=" + group + " tag=" + tag + " topic=" + topic
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

        private static string[] ReadAllHistoryLines(string path)
        {
            if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllLines(path, Encoding.UTF8);

            using (FileStream file = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
            {
                List<string> lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
                return lines.ToArray();
            }
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

            string normalizedPrefix = prefix ?? string.Empty;
            string[] jsonl = System.IO.Directory.GetFiles(directory, normalizedPrefix + "*.jsonl", SearchOption.TopDirectoryOnly);
            string[] gzip = System.IO.Directory.GetFiles(directory, normalizedPrefix + "*.jsonl.gz", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < jsonl.Length; i++)
                files.Add(jsonl[i]);
            for (int i = 0; i < gzip.Length; i++)
                files.Add(gzip[i]);
        }

        private static int CompareHistoryFilesDescending(string left, string right)
        {
            int dateCompare = GetHistoryFileDate(right).CompareTo(GetHistoryFileDate(left));
            return dateCompare != 0 ? dateCompare : string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareHistoryFilesAscending(string left, string right)
        {
            int dateCompare = GetHistoryFileDate(left).CompareTo(GetHistoryFileDate(right));
            return dateCompare != 0 ? dateCompare : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime GetHistoryFileDate(string path)
        {
            string name = Path.GetFileName(path) ?? string.Empty;
            if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);
            if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 6);

            int dash = name.LastIndexOf('-');
            if (dash >= 0 && dash + 1 < name.Length)
            {
                string dateText = name.Substring(dash + 1);
                DateTime parsed;
                if (DateTime.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    return parsed.Date;
            }

            try
            {
                return File.GetLastWriteTime(path).Date;
            }
            catch
            {
                return DateTime.MinValue;
            }
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
            return "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
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
            return (NullToEmpty(snapshot.DeviceName).Trim() + "." + group + "." + NullToEmpty(snapshot.TagName).Trim()).Trim('.');
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
    }
}
