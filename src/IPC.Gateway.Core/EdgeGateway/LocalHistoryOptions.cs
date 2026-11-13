/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：LocalHistoryOptions
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
using IPC.Gateway.DataProcessing;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class LocalHistoryOptions
    {
        public LocalHistoryOptions()
        {
            Enabled = true;
            Directory = "Data\\History";
            RetentionDays = 7;
            MaxViewRecords = 500;
            BatchWriteEnabled = true;
            BatchFlushIntervalMs = 1000;
            BatchMaxLines = 1000;
            BatchQueueLimit = 100000;
            DataProcessing = new EdgeDataProcessingOptions();
            Storage = new LocalHistoryStorageOptions();
        }

        public bool Enabled { get; set; }
        public string Directory { get; set; }
        public int RetentionDays { get; set; }
        public int MaxViewRecords { get; set; }
        public bool BatchWriteEnabled { get; set; }
        public int BatchFlushIntervalMs { get; set; }
        public int BatchMaxLines { get; set; }
        public int BatchQueueLimit { get; set; }
        public EdgeDataProcessingOptions DataProcessing { get; set; }
        public LocalHistoryStorageOptions Storage { get; set; }

        public LocalHistoryOptions Clone()
        {
            int retentionDays = ClampRetentionDays(RetentionDays);
            return new LocalHistoryOptions
            {
                Enabled = Enabled,
                Directory = Directory,
                RetentionDays = retentionDays,
                MaxViewRecords = MaxViewRecords,
                BatchWriteEnabled = BatchWriteEnabled,
                BatchFlushIntervalMs = Clamp(BatchFlushIntervalMs, 100, 60000),
                BatchMaxLines = Clamp(BatchMaxLines, 1, 100000),
                BatchQueueLimit = Clamp(BatchQueueLimit, 1, 1000000),
                DataProcessing = EdgeDataProcessingOptions.Normalize(DataProcessing),
                Storage = LocalHistoryStorageOptions.Normalize(Storage, retentionDays)
            };
        }

        public static int ClampRetentionDays(int value)
        {
            if (value < 1)
                return 7;
            if (value > 3650)
                return 3650;
            return value;
        }

        public static int ClampMaxViewRecords(int value)
        {
            if (value < 50)
                return 50;
            if (value > 10000)
                return 10000;
            return value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }

    public sealed class LocalHistoryStorageOptions
    {
        public const int DefaultValueFileSegmentMegabytes = 64;

        public LocalHistoryStorageOptions()
        {
            TieringEnabled = false;
            ColdDirectory = "Data\\HistoryCold";
            RetentionPolicy = "DeleteOnly";
            HotRetentionDays = 0;
            ColdRetentionDays = 90;
            CompressionEnabled = true;
            CompressHotFiles = true;
            CompressColdFiles = true;
            CompressAfterDays = 0;
            AutoCleanupEnabled = true;
            CleanupIntervalHours = 24;
            MaxStorageMegabytes = 0;
        }

        public bool TieringEnabled { get; set; }
        public string ColdDirectory { get; set; }
        public string RetentionPolicy { get; set; }
        public int HotRetentionDays { get; set; }
        public int ColdRetentionDays { get; set; }
        public bool CompressionEnabled { get; set; }
        public bool CompressHotFiles { get; set; }
        public bool CompressColdFiles { get; set; }
        public int CompressAfterDays { get; set; }
        public bool AutoCleanupEnabled { get; set; }
        public int CleanupIntervalHours { get; set; }
        public int MaxStorageMegabytes { get; set; }
        public int ValueFileSegmentMegabytes { get; set; } = DefaultValueFileSegmentMegabytes;

        public static LocalHistoryStorageOptions Normalize(LocalHistoryStorageOptions? options, int retentionDays)
        {
            LocalHistoryStorageOptions defaults = new LocalHistoryStorageOptions();
            LocalHistoryStorageOptions source = options ?? defaults;
            string policy = string.Equals(source.RetentionPolicy, "MoveToColdThenDelete", StringComparison.OrdinalIgnoreCase)
                ? "MoveToColdThenDelete"
                : "DeleteOnly";
            int hotRetention = Clamp(source.HotRetentionDays <= 0 ? retentionDays : source.HotRetentionDays, 1, 3650);
            int coldRetention = Clamp(source.ColdRetentionDays <= 0 ? Math.Max(retentionDays, hotRetention) : source.ColdRetentionDays, hotRetention, 3650);
            return new LocalHistoryStorageOptions
            {
                TieringEnabled = source.TieringEnabled,
                ColdDirectory = string.IsNullOrWhiteSpace(source.ColdDirectory) ? defaults.ColdDirectory : source.ColdDirectory.Trim(),
                RetentionPolicy = policy,
                HotRetentionDays = hotRetention,
                ColdRetentionDays = coldRetention,
                CompressionEnabled = source.CompressionEnabled,
                CompressHotFiles = source.CompressHotFiles,
                CompressColdFiles = source.CompressColdFiles,
                CompressAfterDays = Clamp(source.CompressAfterDays, 0, 3650),
                AutoCleanupEnabled = source.AutoCleanupEnabled,
                CleanupIntervalHours = Clamp(source.CleanupIntervalHours, 1, 720),
                MaxStorageMegabytes = Clamp(source.MaxStorageMegabytes, 0, 1048576),
                // Older Protobuf backups omit this field and deserialize it as zero.
                ValueFileSegmentMegabytes = source.ValueFileSegmentMegabytes <= 0
                    ? DefaultValueFileSegmentMegabytes : Math.Min(source.ValueFileSegmentMegabytes, 1048576)
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
