/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeDataProcessingStats
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
namespace IPC.Gateway.DataProcessing;

public sealed class EdgeDataProcessingStats
{
    public long ReceivedValueCount { get; set; }
    public long WrittenValueCount { get; set; }
    public long SkippedValueCount { get; set; }
    public long CompressedValueCount { get; set; }
    public long DownsampledValueCount { get; set; }
    public long FilledValueCount { get; set; }
    public long AggregatedValueCount { get; set; }

    public EdgeDataProcessingStats Clone()
    {
        return (EdgeDataProcessingStats)MemberwiseClone();
    }
}
