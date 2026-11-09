/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeDataPoint
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

public sealed class EdgeDataPoint
{
    public EdgeDataPoint()
    {
        TagKey = string.Empty;
        ValueText = string.Empty;
        RawValueText = string.Empty;
        Quality = string.Empty;
        Unit = string.Empty;
        Timestamp = DateTime.MinValue;
    }

    public string TagKey { get; set; }
    public DateTime Timestamp { get; set; }
    public string ValueText { get; set; }
    public string RawValueText { get; set; }
    public string Quality { get; set; }
    public string Unit { get; set; }
    public bool HasNumericValue { get; set; }
    public double NumericValue { get; set; }

    public EdgeDataPoint Clone()
    {
        return (EdgeDataPoint)MemberwiseClone();
    }
}
