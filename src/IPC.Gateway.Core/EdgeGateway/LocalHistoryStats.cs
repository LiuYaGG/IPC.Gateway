/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：LocalHistoryStats
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
namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class LocalHistoryStats
    {
        public LocalHistoryStats()
        {
            Directory = string.Empty;
            ColdDirectory = string.Empty;
            RetentionPolicy = string.Empty;
            LastError = string.Empty;
            CircuitBreaker = new IPC.Gateway.Core.Resilience.CircuitBreakerStatus { Name = "History", Enabled = true };
        }

        public bool Enabled { get; set; }
        public bool IsRunning { get; set; }
        public string Directory { get; set; }
        public int RetentionDays { get; set; }
        public int ValueFiles { get; set; }
        public int AlarmFiles { get; set; }
        public int PublishFiles { get; set; }
        public long TotalBytes { get; set; }
        public string ColdDirectory { get; set; }
        public bool TieringEnabled { get; set; }
        public string RetentionPolicy { get; set; }
        public int HotRetentionDays { get; set; }
        public int ColdRetentionDays { get; set; }
        public bool StorageCompressionEnabled { get; set; }
        public bool AutoCleanupEnabled { get; set; }
        public int CleanupIntervalHours { get; set; }
        public DateTime LastCleanupTime { get; set; }
        public DateTime NextCleanupTime { get; set; }
        public int HotFileCount { get; set; }
        public int ColdFileCount { get; set; }
        public int CompressedFileCount { get; set; }
        public long HotBytes { get; set; }
        public long ColdBytes { get; set; }
        public long CompressedBytes { get; set; }
        public bool DataProcessingEnabled { get; set; }
        public bool CompressionEnabled { get; set; }
        public bool DownsamplingEnabled { get; set; }
        public bool AlignmentEnabled { get; set; }
        public bool FillEnabled { get; set; }
        public bool AggregationEnabled { get; set; }
        public long ReceivedValueCount { get; set; }
        public long WrittenValueCount { get; set; }
        public long SkippedValueCount { get; set; }
        public long CompressedValueCount { get; set; }
        public long DownsampledValueCount { get; set; }
        public long FilledValueCount { get; set; }
        public long AggregatedValueCount { get; set; }
        public bool IsDegraded { get; set; }
        public DateTime LastErrorTime { get; set; }
        public string LastError { get; set; }
        public IPC.Gateway.Core.Resilience.CircuitBreakerStatus CircuitBreaker { get; set; }
    }
}
