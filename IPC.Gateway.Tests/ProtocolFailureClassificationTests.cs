using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Metering;
using IPC.Plc.Communication.MitsubishiMc;
using IPC.Plc.Communication.SiemensS7;

namespace IPC.Gateway.Tests;

public sealed class ProtocolFailureClassificationTests
{
    [Fact]
    public void S7_ReturnCodes_IsolateBadTagsButStopOnHardwareFault()
    {
        Assert.Equal(PlcReadFailureScope.Tag, S7ProtocolErrors.Item(0x05, "read").FailureScope);
        Assert.Equal(PlcReadFailureScope.Device, S7ProtocolErrors.Item(0x01, "read").FailureScope);
        Assert.Equal(PlcReadFailureScope.Session, S7ProtocolErrors.Ack(0x81, 0x04).FailureScope);
    }

    [Fact]
    public void Mc_EndCodes_IsolateAddressErrors()
    {
        Assert.Equal(PlcReadFailureScope.Tag, McProtocolErrors.EndCode(0xC056).FailureScope);
        Assert.Equal(PlcReadFailureScope.Batch, McProtocolErrors.EndCode(0xC061).FailureScope);
        Assert.Equal(PlcReadFailureScope.Device, McProtocolErrors.EndCode(0x4031).FailureScope);
    }

    [Fact]
    public void MeterBatch_StopsChannelOnlyAfterThreeDistinctSilentMeters()
    {
        PlcBatchReadRequest[] requests =
        {
            new("A", PlcDataType.UInt16, 1, 0),
            new("B", PlcDataType.UInt16, 1, 0),
            new("C", PlcDataType.UInt16, 1, 0)
        };

        IList<PlcBatchReadResult> results = MeterBatchReadExecutor.ReadMany(
            requests,
            new MeterBatchReadContext<string>
            {
                ParseAddress = value => value,
                GetAddressKey = value => value,
                ReadRawData = _ => throw new TimeoutException("meter silent"),
                DecodeValue = (_, _, _) => 0,
                TypeName = "meter"
            });

        Assert.Equal(PlcReadFailureScope.Tag, results[0].FailureScope);
        Assert.Equal(PlcReadFailureScope.Tag, results[1].FailureScope);
        Assert.Equal(PlcReadFailureScope.Transport, results[2].FailureScope);
    }

    [Fact]
    public void ConnectionCatalog_ExposesNewBatchAndDiscoveryControls()
    {
        IList<PlcConnectionParameterDefinition> mc = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.MitsubishiMc);
        IList<PlcConnectionParameterDefinition> bacnet = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.BacnetIp);
        IList<PlcConnectionParameterDefinition> opcDa = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.OpcDa);

        Assert.Contains(mc, item => item.Key == "driverOptions.mcMaxBatchGapPoints");
        Assert.Contains(bacnet, item => item.Key == "driverOptions.deviceInstance");
        Assert.Contains(bacnet, item => item.Key == "driverOptions.bbmdAddress");
        Assert.Contains(opcDa, item => item.Key == "driverOptions.opcDaReadSource");
    }
}
