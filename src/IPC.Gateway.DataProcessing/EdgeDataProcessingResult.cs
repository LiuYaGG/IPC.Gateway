/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeDataProcessingResult
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

public sealed class EdgeDataProcessingResult
{
    public EdgeDataProcessingResult()
    {
        DerivedPoints = new List<EdgeProcessedDataPoint>();
        SkipReason = string.Empty;
    }

    public bool WriteCurrent { get; set; }
    public EdgeProcessedDataPoint? Current { get; set; }
    public IList<EdgeProcessedDataPoint> DerivedPoints { get; }
    public string SkipReason { get; set; }
    public bool CompressionSkipped { get; set; }
    public bool DownsamplingSkipped { get; set; }
}
