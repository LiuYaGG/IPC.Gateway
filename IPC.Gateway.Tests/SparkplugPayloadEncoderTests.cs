/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：SparkplugPayloadEncoderTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Gateway.Mqtt.Sparkplug;

namespace IPC.Gateway.Tests;

public sealed class SparkplugPayloadEncoderTests
{
    [Fact]
    public void TopicBuilder_BuildsSparkplugNamespaceTopics()
    {
        SparkplugTopicBuilder builder = new SparkplugTopicBuilder("spBv1.0", "Line/A", "Gateway-01");

        Assert.Equal("spBv1.0/Line/A/NBIRTH/Gateway-01", builder.NodeBirth());
        Assert.Equal("spBv1.0/Line/A/NDEATH/Gateway-01", builder.NodeDeath());
        Assert.Equal("spBv1.0/Line/A/DDATA/Gateway-01/PLC-01", builder.DeviceData("PLC-01"));
    }

    [Fact]
    public void Encoder_ProducesSparkplugPayloadBytes()
    {
        SparkplugPayload payload = new SparkplugPayload
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000),
            Sequence = 1
        };
        SparkplugMetric metric = SparkplugMetric.FromText("Pump/Speed", "Double", "12.5");
        metric.Properties["unit"] = "Hz";
        metric.Properties["quality"] = "Good";
        payload.Metrics.Add(SparkplugMetric.UInt64("bdSeq", 1));
        payload.Metrics.Add(metric);

        byte[] bytes = SparkplugPayloadEncoder.Encode(payload);

        Assert.NotEmpty(bytes);
        Assert.Contains((byte)'P', bytes);
        Assert.Contains((byte)'H', bytes);
    }
}
