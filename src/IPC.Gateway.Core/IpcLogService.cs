/*----------------------------------------------------------------
* 项目名称 ：IPC
* 项目描述 ：
* 类 名 称 ：IpcLogService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPC
{
    
    
    
    
    
    
    
    
    
    internal static class IpcLogService
    {
        private const int MaxQueueLength = 50000;
        private const int MaxBatchSize = 512;
        private static readonly object SyncRoot = new object();
        private static readonly object QueueSyncRoot = new object();
        private static readonly Queue<LogWriteItem> PendingWrites = new Queue<LogWriteItem>();
        private static readonly ManualResetEventSlim WorkerStopped = new ManualResetEventSlim(false);
        private static bool _workerStarted;
        private static bool _shutdownRequested;
        private static int _retentionDays = 7;
        private static DateTime _lastCleanupDate = DateTime.MinValue;
        private static bool _queueFullWarningPending;

        static IpcLogService()
        {
            StartWorker();
            AppDomain.CurrentDomain.ProcessExit += delegate { ShutdownWorker(); };
            AppDomain.CurrentDomain.DomainUnload += delegate { ShutdownWorker(); };
        }

        public static void ConfigureRetentionDays(int retentionDays)
        {
            lock (SyncRoot)
            {
                _retentionDays = ClampRetentionDays(retentionDays);
            }
        }

        public static int ClampRetentionDays(int retentionDays)
        {
            if (retentionDays < 1)
                return 7;
            if (retentionDays > 3650)
                return 3650;
            return retentionDays;
        }

        public static void WriteInfo(string message)
        {
            Write("app", "INFO", message);
        }

        public static void WriteWarning(string message)
        {
            Write("app", "WARN", message);
        }

        public static void WriteError(string message)
        {
            Write("app", "ERROR", message);
        }

        public static void WriteError(string message, Exception? exception)
        {
            Write("app", "ERROR", message + BuildExceptionText(exception));
        }

        public static void WriteAudit(string action, string target, string detail)
        {
            string message = "action=" + Safe(action) + " target=" + Safe(target) + " detail=" + Safe(detail);
            Write("audit", "AUDIT", message);
        }

        public static void Cleanup()
        {
            lock (SyncRoot)
            {
                CleanupCore();
            }
        }

        private static void Write(string category, string level, string message)
        {
            LogWriteItem item = new LogWriteItem(category, level, Safe(message), DateTime.Now);
            bool writeSynchronously = false;
            lock (QueueSyncRoot)
            {
                if (_shutdownRequested || PendingWrites.Count >= MaxQueueLength)
                {
                    writeSynchronously = true;
                    _queueFullWarningPending = PendingWrites.Count >= MaxQueueLength;
                }
                else
                {
                    PendingWrites.Enqueue(item);
                    Monitor.Pulse(QueueSyncRoot);
                }
            }

            if (writeSynchronously)
                WriteBatchSync(new[] { item });
        }

        private static void StartWorker()
        {
            lock (QueueSyncRoot)
            {
                if (_workerStarted)
                    return;

                _workerStarted = true;
                Thread worker = new Thread(ProcessQueue);
                worker.IsBackground = true;
                worker.Name = "IPC Log Writer";
                worker.Start();
            }
        }

        private static void ShutdownWorker()
        {
            lock (QueueSyncRoot)
            {
                if (_shutdownRequested)
                    return;

                _shutdownRequested = true;
                Monitor.PulseAll(QueueSyncRoot);
            }

            WorkerStopped.Wait(3000);
            FlushPendingSync();
        }

        private static void ProcessQueue()
        {
            try
            {
                while (true)
                {
                    List<LogWriteItem> batch;
                    lock (QueueSyncRoot)
                    {
                        while (!_shutdownRequested && PendingWrites.Count == 0)
                            Monitor.Wait(QueueSyncRoot);

                        if (_shutdownRequested && PendingWrites.Count == 0)
                            return;

                        batch = DequeueBatchNoLock();
                    }

                    try
                    {
                        WriteBatchAsync(batch).GetAwaiter().GetResult();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                WorkerStopped.Set();
            }
        }

        private static void FlushPendingSync()
        {
            while (true)
            {
                List<LogWriteItem> batch;
                lock (QueueSyncRoot)
                {
                    if (PendingWrites.Count == 0)
                        return;

                    batch = DequeueBatchNoLock();
                }

                WriteBatchSync(batch);
            }
        }

        private static List<LogWriteItem> DequeueBatchNoLock()
        {
            int count = Math.Min(PendingWrites.Count, MaxBatchSize);
            List<LogWriteItem> batch = new List<LogWriteItem>(count);
            for (int i = 0; i < count; i++)
                batch.Add(PendingWrites.Dequeue());
            return batch;
        }

        private static async Task WriteBatchAsync(IList<LogWriteItem> batch)
        {
            if (batch == null || batch.Count == 0)
                return;

            string directory = PrepareLogDirectory();
            Dictionary<string, StringBuilder> linesByPath = BuildLinesByPath(directory, batch);
            foreach (KeyValuePair<string, StringBuilder> pair in linesByPath)
                await AppendTextAsync(pair.Key, pair.Value.ToString());
        }

        private static void WriteBatchSync(IList<LogWriteItem> batch)
        {
            if (batch == null || batch.Count == 0)
                return;

            string directory = PrepareLogDirectory();
            Dictionary<string, StringBuilder> linesByPath = BuildLinesByPath(directory, batch);
            foreach (KeyValuePair<string, StringBuilder> pair in linesByPath)
                AppendTextSync(pair.Key, pair.Value.ToString());
        }

        private static string PrepareLogDirectory()
        {
            lock (SyncRoot)
            {
                CleanupCore();
            }

            string directory = GetLogDirectoryPath();
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static Dictionary<string, StringBuilder> BuildLinesByPath(string directory, IList<LogWriteItem> batch)
        {
            Dictionary<string, StringBuilder> linesByPath = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < batch.Count; i++)
            {
                LogWriteItem item = batch[i];
                string category = SafeFilePart(item.Category);
                string path = Path.Combine(directory, category + "-" + item.Timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                StringBuilder? builder;
                if (!linesByPath.TryGetValue(path, out builder))
                {
                    builder = new StringBuilder();
                    linesByPath[path] = builder;
                }

                builder.Append(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.Append(" [");
                builder.Append(item.Level);
                builder.Append("] ");
                builder.Append(item.Message);
                builder.AppendLine();
            }

            bool includeQueueWarning = false;
            lock (QueueSyncRoot)
            {
                if (_queueFullWarningPending)
                {
                    _queueFullWarningPending = false;
                    includeQueueWarning = true;
                }
            }

            if (includeQueueWarning)
            {
                string path = Path.Combine(directory, "app-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                StringBuilder? builder;
                if (!linesByPath.TryGetValue(path, out builder))
                {
                    builder = new StringBuilder();
                    linesByPath[path] = builder;
                }

                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.AppendLine(" [WARN] Log queue is full; writing some log entries synchronously.");
            }

            return linesByPath;
        }

        private static async Task AppendTextAsync(string path, string text)
        {
            UTF8Encoding encoding = new UTF8Encoding(true);
            bool writePreamble = !File.Exists(path) || new FileInfo(path).Length == 0;
            using FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            if (writePreamble)
            {
                byte[] preamble = encoding.GetPreamble();
                if (preamble.Length > 0)
                    await stream.WriteAsync(preamble, 0, preamble.Length);
            }

            byte[] bytes = encoding.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static void AppendTextSync(string path, string text)
        {
            UTF8Encoding encoding = new UTF8Encoding(true);
            bool writePreamble = !File.Exists(path) || new FileInfo(path).Length == 0;
            using FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            if (writePreamble)
            {
                byte[] preamble = encoding.GetPreamble();
                if (preamble.Length > 0)
                    stream.Write(preamble, 0, preamble.Length);
            }

            byte[] bytes = encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void CleanupCore()
        {
            DateTime today = DateTime.Today;
            if (_lastCleanupDate == today)
                return;

            _lastCleanupDate = today;
            string directory = GetLogDirectoryPath();
            if (!Directory.Exists(directory))
                return;

            DateTime threshold = today.AddDays(-_retentionDays);
            string[] files = Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                try
                {
                    DateTime lastWriteTime = File.GetLastWriteTime(file);
                    if (lastWriteTime < threshold)
                        File.Delete(file);
                }
                catch
                {
                }
            }
        }

        private static string GetLogDirectoryPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Logs");
        }

        private static string BuildExceptionText(Exception? exception)
        {
            if (exception == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            Exception? current = exception;
            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Append(" | ");
                builder.Append(current.GetType().Name);
                builder.Append(": ");
                builder.Append(Safe(current.Message));
                current = current.InnerException;
            }
            return " " + builder.ToString();
        }

        private static string Safe(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ");
        }

        private static string SafeFilePart(string? value)
        {
            string text = Safe(value);
            if (string.IsNullOrWhiteSpace(text))
                return "app";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                text = text.Replace(invalid, '_');
            return text;
        }

        private sealed class LogWriteItem
        {
            public LogWriteItem(string category, string level, string message, DateTime timestamp)
            {
                Category = category ?? string.Empty;
                Level = level ?? string.Empty;
                Message = message ?? string.Empty;
                Timestamp = timestamp;
            }

            public string Category { get; private set; }
            public string Level { get; private set; }
            public string Message { get; private set; }
            public DateTime Timestamp { get; private set; }
        }
    }
}
