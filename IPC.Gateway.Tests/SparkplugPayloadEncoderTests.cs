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
        Assert.Equal("spBv1.0/Line/A/NCMD/Gateway-01", builder.NodeCommand());
        Assert.Equal("spBv1.0/Line/A/DCMD/Gateway-01/+", builder.DeviceCommandFilter());
        Assert.Equal("spBv1.0/STATE/Primary-01", builder.PrimaryHostState("Primary-01"));
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

    [Fact]
    public void EncoderDecoder_RoundTripsComplexSparkplugTypes()
    {
        SparkplugPayload payload = new()
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(123456),
            Sequence = 7,
            Uuid = "batch-01",
            Body = new byte[] { 1, 2, 3 }
        };

        SparkplugMetric dataSetMetric = new()
        {
            Name = "History",
            DataType = SparkplugDataType.DataSet,
            MetaData = new SparkplugMetaData
            {
                ContentType = "application/x-sparkplug-dataset",
                Description = "history"
            }
        };
        dataSetMetric.TypedProperties.Values["enabled"] = new SparkplugPropertyValue
        {
            DataType = SparkplugDataType.Boolean,
            Value = true
        };
        dataSetMetric.DataSetValue = new SparkplugDataSet { NumberOfColumns = 2 };
        dataSetMetric.DataSetValue.Columns.Add("name");
        dataSetMetric.DataSetValue.Columns.Add("value");
        dataSetMetric.DataSetValue.Types.Add(SparkplugDataType.String);
        dataSetMetric.DataSetValue.Types.Add(SparkplugDataType.Double);
        SparkplugDataSetRow row = new();
        row.Values.Add("temperature");
        row.Values.Add(21.5D);
        dataSetMetric.DataSetValue.Rows.Add(row);

        SparkplugMetric templateMetric = new()
        {
            Name = "Motor",
            DataType = SparkplugDataType.Template,
            TemplateValue = new SparkplugTemplate
            {
                Version = "1",
                TemplateReference = "MotorTemplate",
                IsDefinition = true
            }
        };
        templateMetric.TemplateValue.Metrics.Add(SparkplugMetric.Boolean("Running", true));
        templateMetric.TemplateValue.Parameters.Add(new SparkplugTemplateParameter
        {
            Name = "PoleCount",
            DataType = SparkplugDataType.Int32,
            Value = 4
        });

        SparkplugMetric arrayMetric = new()
        {
            Name = "Samples",
            DataType = SparkplugDataType.Int16Array,
            Value = new short[] { -1, 2, 3 }
        };
        payload.Metrics.Add(dataSetMetric);
        payload.Metrics.Add(templateMetric);
        payload.Metrics.Add(arrayMetric);

        SparkplugPayload decoded = SparkplugPayloadDecoder.Decode(SparkplugPayloadEncoder.Encode(payload));

        Assert.Equal(payload.Timestamp, decoded.Timestamp);
        Assert.Equal(7U, decoded.Sequence);
        Assert.Equal("batch-01", decoded.Uuid);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Body);

        SparkplugMetric decodedDataSet = Assert.Single(decoded.Metrics, metric => metric.Name == "History");
        Assert.Equal(true, decodedDataSet.TypedProperties.Values["enabled"].Value);
        Assert.Equal("history", decodedDataSet.MetaData?.Description);
        Assert.Equal("temperature", decodedDataSet.DataSetValue?.Rows[0].Values[0]);
        Assert.Equal(21.5D, decodedDataSet.DataSetValue?.Rows[0].Values[1]);

        SparkplugMetric decodedTemplate = Assert.Single(decoded.Metrics, metric => metric.Name == "Motor");
        Assert.True(decodedTemplate.TemplateValue?.IsDefinition);
        Assert.Equal("MotorTemplate", decodedTemplate.TemplateValue?.TemplateReference);
        Assert.Equal(4, decodedTemplate.TemplateValue?.Parameters[0].Value);

        SparkplugMetric decodedArray = Assert.Single(decoded.Metrics, metric => metric.Name == "Samples");
        Assert.Equal(new object?[] { (short)-1, (short)2, (short)3 }, Assert.IsType<object?[]>(decodedArray.Value));
    }
}
