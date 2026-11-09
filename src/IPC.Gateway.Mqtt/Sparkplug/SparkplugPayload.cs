/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Mqtt.Sparkplug
* 项目描述 ：
* 类 名 称 ：SparkplugPayload
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Mqtt.Sparkplug
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
namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugPayload
{
    public SparkplugPayload()
    {
        Timestamp = DateTimeOffset.UtcNow;
        Metrics = new List<SparkplugMetric>();
    }

    public DateTimeOffset Timestamp { get; set; }
    public uint Sequence { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public byte[] Body { get; set; } = Array.Empty<byte>();
    public IList<SparkplugMetric> Metrics { get; }
}
