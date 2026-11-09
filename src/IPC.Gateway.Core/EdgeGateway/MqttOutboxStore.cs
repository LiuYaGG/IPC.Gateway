/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttOutboxStore
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
using System.Text;
using System.Threading.Tasks;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    internal sealed class MqttOutboxStore
    {
        private readonly object _syncRoot;
        private readonly string _directoryPath;
        private long _lastId;

        public MqttOutboxStore(string directoryPath)
        {
            _syncRoot = new object();
            _directoryPath = directoryPath;
            Directory.CreateDirectory(_directoryPath);
            _lastId = FindLastId();
        }

        public string DirectoryPath
        {
            get { return _directoryPath; }
        }

        public string QuarantineDirectoryPath
        {
            get { return Path.Combine(_directoryPath, "Corrupt"); }
        }

        public MqttOutboxMessage Enqueue(string topic, string payload, int qos)
        {
            return EnqueueText(topic, payload, qos);
        }

        public Task<MqttOutboxMessage> EnqueueAsync(string topic, string payload, int qos)
        {
            return EnqueueTextAsync(topic, payload, qos);
        }

        public MqttOutboxMessage Enqueue(string topic, byte[] payload, int qos)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("MQTT outbox topic is empty.", "topic");

            return EnqueueCore(new MqttOutboxMessage
            {
                Topic = topic,
                Payload = string.Empty,
                PayloadBytes = payload ?? Array.Empty<byte>(),
                PayloadFormat = "Binary",
                Qos = MqttGatewayOptions.ClampQos(qos),
                CreatedAt = DateTime.Now
            });
        }

        public Task<MqttOutboxMessage> EnqueueAsync(string topic, byte[] payload, int qos)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("MQTT outbox topic is empty.", "topic");

            return EnqueueCoreAsync(new MqttOutboxMessage
            {
                Topic = topic,
                Payload = string.Empty,
                PayloadBytes = payload ?? Array.Empty<byte>(),
                PayloadFormat = "Binary",
                Qos = MqttGatewayOptions.ClampQos(qos),
                CreatedAt = DateTime.Now
            });
        }

        private MqttOutboxMessage EnqueueText(string topic, string payload, int qos)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("MQTT outbox topic is empty.", "topic");

            return EnqueueCore(new MqttOutboxMessage
            {
                Topic = topic,
                Payload = payload ?? string.Empty,
                PayloadBytes = System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty),
                PayloadFormat = "Text",
                Qos = MqttGatewayOptions.ClampQos(qos),
                CreatedAt = DateTime.Now
            });
        }

        private Task<MqttOutboxMessage> EnqueueTextAsync(string topic, string payload, int qos)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("MQTT outbox topic is empty.", "topic");

            return EnqueueCoreAsync(new MqttOutboxMessage
            {
                Topic = topic,
                Payload = payload ?? string.Empty,
                PayloadBytes = Encoding.UTF8.GetBytes(payload ?? string.Empty),
                PayloadFormat = "Text",
                Qos = MqttGatewayOptions.ClampQos(qos),
                CreatedAt = DateTime.Now
            });
        }

        private MqttOutboxMessage EnqueueCore(MqttOutboxMessage message)
        {
            lock (_syncRoot)
            {
                long id = NextId();
                message.Id = id;

                string finalPath = BuildPath(id, ".msg");
                string tempPath = BuildPath(id, ".tmp");
                WriteAllTextAsync(tempPath, message.ToFileText()).GetAwaiter().GetResult();
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(tempPath, finalPath);
                return message;
            }
        }

        private async Task<MqttOutboxMessage> EnqueueCoreAsync(MqttOutboxMessage message)
        {
            long id;
            lock (_syncRoot)
            {
                id = NextId();
                message.Id = id;
            }

            string finalPath = BuildPath(id, ".msg");
            string tempPath = BuildPath(id, ".tmp");
            await WriteAllTextAsync(tempPath, message.ToFileText());

            lock (_syncRoot)
            {
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            }

            return message;
        }

        public IList<MqttOutboxEntry> ListPending(int maxCount)
        {
            lock (_syncRoot)
            {
                List<MqttOutboxEntry> entries = new List<MqttOutboxEntry>();
                string[] files = Directory.GetFiles(_directoryPath, "*.msg");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    if (maxCount > 0 && entries.Count >= maxCount)
                        break;

                    try
                    {
                        string text = File.ReadAllText(files[i], System.Text.Encoding.UTF8);
                        MqttOutboxMessage? message;
                        if (MqttOutboxMessage.TryParse(text, out message))
                            entries.Add(new MqttOutboxEntry(files[i], message, GetFileLength(files[i])));
                    }
                    catch
                    {
                    }
                }
                return entries;
            }
        }

        public void Delete(MqttOutboxEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
                return;

            lock (_syncRoot)
            {
                if (File.Exists(entry.FilePath))
                    File.Delete(entry.FilePath);
            }
        }

        public int CountPending()
        {
            lock (_syncRoot)
                return Directory.GetFiles(_directoryPath, "*.msg").Length;
        }

        public MqttOutboxStats GetStats()
        {
            lock (_syncRoot)
            {
                MqttOutboxStats stats = new MqttOutboxStats();
                string[] files = Directory.GetFiles(_directoryPath, "*.msg");
                stats.MessageCount = files.Length;
                for (int i = 0; i < files.Length; i++)
                {
                    stats.TotalBytes += GetFileLength(files[i]);

                    try
                    {
                        string text = File.ReadAllText(files[i], System.Text.Encoding.UTF8);
                        MqttOutboxMessage? message;
                        if (MqttOutboxMessage.TryParse(text, out message))
                        {
                            if (stats.OldestCreatedAt == DateTime.MinValue || message.CreatedAt < stats.OldestCreatedAt)
                                stats.OldestCreatedAt = message.CreatedAt;
                            if (stats.NewestCreatedAt == DateTime.MinValue || message.CreatedAt > stats.NewestCreatedAt)
                                stats.NewestCreatedAt = message.CreatedAt;
                        }
                        else
                        {
                            stats.InvalidMessageCount++;
                        }
                    }
                    catch
                    {
                        stats.InvalidMessageCount++;
                    }
                }
                return stats;
            }
        }

        public int DeleteByTopicPrefix(string topicPrefix)
        {
            if (string.IsNullOrWhiteSpace(topicPrefix))
                return 0;

            lock (_syncRoot)
            {
                int deleted = 0;
                List<MqttOutboxEntry> entries = LoadAllEntries();
                foreach (MqttOutboxEntry entry in entries)
                {
                    if (!entry.Message.Topic.StartsWith(topicPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    DeleteFile(entry.FilePath);
                    deleted++;
                }
                return deleted;
            }
        }

        public MqttOutboxCleanupResult Cleanup(int maxMessages, long maxBytes, TimeSpan retention)
        {
            return Cleanup(maxMessages, maxBytes, retention, retention);
        }

        public MqttOutboxCleanupResult Cleanup(int maxMessages, long maxBytes, TimeSpan retention, TimeSpan quarantineRetention)
        {
            lock (_syncRoot)
            {
                MqttOutboxCleanupResult result = new MqttOutboxCleanupResult();
                result.InvalidQuarantined = QuarantineInvalidFiles();
                result.QuarantineExpiredDeleted = CleanupQuarantineFiles(quarantineRetention);
                List<MqttOutboxEntry> entries = LoadAllEntries();
                DateTime expireBefore = DateTime.Now.Subtract(retention);

                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    MqttOutboxEntry entry = entries[i];
                    if (entry.Message.CreatedAt < expireBefore)
                    {
                        DeleteFile(entry.FilePath);
                        result.ExpiredDeleted++;
                        entries.RemoveAt(i);
                    }
                }

                long totalBytes = 0;
                for (int i = 0; i < entries.Count; i++)
                    totalBytes += entries[i].Length;

                int maxCount = maxMessages <= 0 ? int.MaxValue : maxMessages;
                long maxSize = maxBytes <= 0 ? long.MaxValue : maxBytes;
                int index = 0;
                while ((entries.Count > maxCount || totalBytes > maxSize) && index < entries.Count)
                {
                    MqttOutboxEntry entry = entries[index];
                    DeleteFile(entry.FilePath);
                    totalBytes -= entry.Length;
                    result.OverflowDeleted++;
                    entries.RemoveAt(index);
                }

                result.RemainingCount = entries.Count;
                result.RemainingBytes = Math.Max(0, totalBytes);
                MqttOutboxQuarantineStats quarantineStats = BuildQuarantineStats();
                result.RemainingQuarantineCount = quarantineStats.MessageCount;
                result.RemainingQuarantineBytes = quarantineStats.TotalBytes;
                return result;
            }
        }

        public MqttOutboxQuarantineStats GetQuarantineStats()
        {
            lock (_syncRoot)
                return BuildQuarantineStats();
        }

        private long NextId()
        {
            long nowId = long.Parse(DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            long next = Math.Max(nowId, _lastId + 1);
            _lastId = next;
            return next;
        }

        private long FindLastId()
        {
            long last = 0;
            string[] files = Directory.GetFiles(_directoryPath, "*.msg");
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                long id;
                if (long.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > last)
                    last = id;
            }
            return last;
        }

        private string BuildPath(long id, string extension)
        {
            return Path.Combine(_directoryPath, id.ToString("00000000000000000", CultureInfo.InvariantCulture) + extension);
        }

        private List<MqttOutboxEntry> LoadAllEntries()
        {
            List<MqttOutboxEntry> entries = new List<MqttOutboxEntry>();
            string[] files = Directory.GetFiles(_directoryPath, "*.msg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string text = File.ReadAllText(files[i], System.Text.Encoding.UTF8);
                    MqttOutboxMessage? message;
                    if (MqttOutboxMessage.TryParse(text, out message))
                        entries.Add(new MqttOutboxEntry(files[i], message, GetFileLength(files[i])));
                }
                catch
                {
                }
            }
            return entries;
        }

        private int QuarantineInvalidFiles()
        {
            int quarantined = 0;
            string[] files = Directory.GetFiles(_directoryPath, "*.msg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (IsValidMessageFile(files[i]))
                    continue;

                if (MoveToQuarantine(files[i]))
                    quarantined++;
            }

            return quarantined;
        }

        private static bool IsValidMessageFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                MqttOutboxMessage? message;
                return MqttOutboxMessage.TryParse(text, out message);
            }
            catch
            {
                return false;
            }
        }

        private bool MoveToQuarantine(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            Directory.CreateDirectory(QuarantineDirectoryPath);
            string fileName = Path.GetFileName(path);
            string targetPath = Path.Combine(QuarantineDirectoryPath, fileName);
            if (File.Exists(targetPath))
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                targetPath = Path.Combine(
                    QuarantineDirectoryPath,
                    name + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + extension);
            }

            try
            {
                File.Move(path, targetPath);
                File.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int CleanupQuarantineFiles(TimeSpan retention)
        {
            if (!Directory.Exists(QuarantineDirectoryPath))
                return 0;

            int deleted = 0;
            DateTime expireBeforeUtc = DateTime.UtcNow.Subtract(retention <= TimeSpan.Zero ? TimeSpan.FromHours(1) : retention);
            string[] files = Directory.GetFiles(QuarantineDirectoryPath, "*.msg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (GetFileLastWriteTimeUtc(files[i]) >= expireBeforeUtc)
                    continue;

                try
                {
                    DeleteFile(files[i]);
                    deleted++;
                }
                catch
                {
                }
            }

            return deleted;
        }

        private MqttOutboxQuarantineStats BuildQuarantineStats()
        {
            MqttOutboxQuarantineStats stats = new MqttOutboxQuarantineStats();
            if (!Directory.Exists(QuarantineDirectoryPath))
                return stats;

            string[] files = Directory.GetFiles(QuarantineDirectoryPath, "*.msg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            stats.MessageCount = files.Length;
            for (int i = 0; i < files.Length; i++)
            {
                stats.TotalBytes += GetFileLength(files[i]);
                DateTime quarantineTime = GetFileLastWriteTime(files[i]);
                if (stats.OldestQuarantineTime == DateTime.MinValue || quarantineTime < stats.OldestQuarantineTime)
                    stats.OldestQuarantineTime = quarantineTime;
                if (stats.NewestQuarantineTime == DateTime.MinValue || quarantineTime > stats.NewestQuarantineTime)
                    stats.NewestQuarantineTime = quarantineTime;
            }

            return stats;
        }

        private static long GetFileLength(string path)
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

        private static DateTime GetFileLastWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static DateTime GetFileLastWriteTimeUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static void DeleteFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }

        private static async Task WriteAllTextAsync(string path, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            byte[] preamble = Encoding.UTF8.GetPreamble();
            if (preamble.Length > 0)
                await stream.WriteAsync(preamble, 0, preamble.Length);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }
    }

    
    
    
    
    
    
    
    
    
    internal sealed class MqttOutboxEntry
    {
        public MqttOutboxEntry(string filePath, MqttOutboxMessage message, long length)
        {
            FilePath = filePath;
            Message = message;
            Length = length;
        }

        public string FilePath { get; private set; }
        public MqttOutboxMessage Message { get; private set; }
        public long Length { get; private set; }
    }

    
    
    
    
    
    
    
    
    
    internal sealed class MqttOutboxStats
    {
        public int MessageCount { get; set; }
        public long TotalBytes { get; set; }
        public int InvalidMessageCount { get; set; }
        public DateTime OldestCreatedAt { get; set; }
        public DateTime NewestCreatedAt { get; set; }
    }

    internal sealed class MqttOutboxQuarantineStats
    {
        public int MessageCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTime OldestQuarantineTime { get; set; }
        public DateTime NewestQuarantineTime { get; set; }
    }

    
    
    
    
    
    
    
    
    
    internal sealed class MqttOutboxCleanupResult
    {
        public int ExpiredDeleted { get; set; }
        public int OverflowDeleted { get; set; }
        public int InvalidQuarantined { get; set; }
        public int QuarantineExpiredDeleted { get; set; }
        public int RemainingCount { get; set; }
        public long RemainingBytes { get; set; }
        public int RemainingQuarantineCount { get; set; }
        public long RemainingQuarantineBytes { get; set; }
    }
}
