using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Gateway.Mqtt.Sparkplug;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Mqtt;

namespace IPC.Gateway.Tests;

public sealed class MqttSouthboundConfigurationTests
{
    [Fact]
    public void ConnectionParameters_ExposeSubscriptionPayloadAndTlsOptions()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.MqttClient);

        Assert.Contains(parameters, item => item.Key == "driverOptions.mqttSubscribeFilter");
        Assert.Contains(parameters, item => item.Key == "driverOptions.mqttPayloadMode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.mqttUseTls");
        Assert.Contains(parameters, item => item.Key == "driverOptions.mqttMaxValueAgeSeconds");
    }

    [Theory]
    [InlineData("factory/line1/value", "factory/line1/value", "")]
    [InlineData("factory/line1/data|temperature", "factory/line1/data", "temperature")]
    public void Address_ParsesTopicAndSelector(string address, string topic, string selector)
    {
        MqttTagAddress parsed = MqttTagAddress.Parse(address);

        Assert.Equal(topic, parsed.Topic);
        Assert.Equal(selector, parsed.Selector);
    }

    [Theory]
    [InlineData("")]
    [InlineData("factory/+/value")]
    [InlineData("factory/#")]
    [InlineData("factory/value|")]
    public void Address_RejectsInvalidTagTopic(string address)
    {
        Assert.Throws<FormatException>(() => MqttTagAddress.Parse(address));
    }

    [Fact]
    public void JsonPayload_DecodesScalarAndArray()
    {
        const string json = "{\"temperature\":12.5,\"values\":[10,20,30,40]}";

        Assert.Equal(12.5f, MqttPayloadCodec.DecodeText(json, "temperature", PlcDataType.Float, 1, 0, true));
        Assert.Equal(new[] { 20, 30 }, MqttPayloadCodec.DecodeText(json, "values", PlcDataType.Int32Array, 2, 1, true));
    }

    [Fact]
    public void SparkplugPayload_RoundTripsMetricValues()
    {
        SparkplugPayload source = new SparkplugPayload { Sequence = 7 };
        source.Metrics.Add(new SparkplugMetric
        {
            Name = "temperature",
            Alias = 3,
            DataType = SparkplugDataType.Double,
            Value = 23.75d
        });

        SparkplugPayload decoded = SparkplugPayloadDecoder.Decode(SparkplugPayloadEncoder.Encode(source));

        SparkplugMetric metric = Assert.Single(decoded.Metrics);
        Assert.Equal((uint)7, decoded.Sequence);
        Assert.Equal("temperature", metric.Name);
        Assert.Equal((ulong)3, metric.Alias);
        Assert.Equal(23.75d, metric.Value);
    }

    [Fact]
    public void Driver_CreatesSouthboundClientWithoutConnecting()
    {
        using IPlcClient client = new MqttSouthboundProtocolDriver().CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.MqttClient,
            Host = "127.0.0.1",
            Port = 1883,
            DriverOptionsJson = "{\"mqttSubscribeFilter\":\"factory/#\",\"mqttPayloadMode\":\"Json\"}"
        });

        Assert.Equal(PlcProtocol.MqttClient, client.Protocol);
    }
}
