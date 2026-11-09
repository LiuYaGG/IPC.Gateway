/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeProcessedDataPoint
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

public sealed class EdgeProcessedDataPoint
{
    public EdgeProcessedDataPoint()
    {
        Point = new EdgeDataPoint();
        ProcessingType = "raw";
        Reason = string.Empty;
        AggregateMethod = string.Empty;
        OriginalTimestamp = DateTime.MinValue;
        WindowStart = DateTime.MinValue;
        WindowEnd = DateTime.MinValue;
    }

    public EdgeDataPoint Point { get; set; }
    public string ProcessingType { get; set; }
    public string Reason { get; set; }
    public DateTime OriginalTimestamp { get; set; }
    public string AggregateMethod { get; set; }
    public int SampleCount { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
}
