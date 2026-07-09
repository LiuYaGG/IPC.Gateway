using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Gateway.Tests;

public sealed class ModbusBatchReadExecutorTests
{
    [Fact]
    public void ReadMany_SplitsNonCommunicationSegmentFailureAndKeepsHealthyPoints()
    {
        List<(int Start, int Count)> registerReads = new List<(int Start, int Count)>();
        IList<PlcBatchReadRequest> requests = new List<PlcBatchReadRequest>
        {
            new PlcBatchReadRequest("HR0", PlcDataType.Int16, 1, 0),
            new PlcBatchReadRequest("HR1", PlcDataType.Int16, 1, 0),
            new PlcBatchReadRequest("HR2", PlcDataType.Int16, 1, 0)
        };

        ModbusBatchReadContext context = CreateRegisterContext(delegate(ModbusArea area, int start, int count)
        {
            registerReads.Add((start, count));
            if (start <= 1 && start + count - 1 >= 1)
                throw new InvalidOperationException("Illegal data address.");

            return BuildRegisterBytes(start, count);
        });

        IList<PlcBatchReadResult> results = ModbusBatchReadExecutor.ReadMany(requests, context);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.False(results[1].IsCommunicationError);
        Assert.True(results[2].Success);
        Assert.Equal((short)10, results[0].Result!.Value);
        Assert.Equal((short)12, results[2].Result!.Value);
        Assert.Contains(registerReads, read => read.Start == 0 && read.Count == 3);
        Assert.Contains(registerReads, read => read.Start == 0 && read.Count == 1);
        Assert.Contains(registerReads, read => read.Start == 1 && read.Count == 1);
        Assert.Contains(registerReads, read => read.Start == 2 && read.Count == 1);
    }

    [Fact]
    public void ReadMany_DoesNotSplitCommunicationSegmentFailure()
    {
        int registerReadCount = 0;
        IList<PlcBatchReadRequest> requests = new List<PlcBatchReadRequest>
        {
            new PlcBatchReadRequest("HR0", PlcDataType.Int16, 1, 0),
            new PlcBatchReadRequest("HR1", PlcDataType.Int16, 1, 0)
        };

        ModbusBatchReadContext context = CreateRegisterContext(delegate
        {
            registerReadCount++;
            throw new TimeoutException("Read timed out.");
        });

        IList<PlcBatchReadResult> results = ModbusBatchReadExecutor.ReadMany(requests, context);

        Assert.Equal(1, registerReadCount);
        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.True(result.IsCommunicationError);
        });
    }

    private static ModbusBatchReadContext CreateRegisterContext(Func<ModbusArea, int, int, byte[]> readRegisters)
    {
        return new ModbusBatchReadContext
        {
            ReadBits = delegate { throw new NotSupportedException(); },
            ReadRegisters = readRegisters,
            GetTypeCode = delegate { return 3; },
            GetTypeName = delegate { return "HR"; },
            MaxReadBits = 2000,
            MaxReadRegisters = 125
        };
    }

    private static byte[] BuildRegisterBytes(int start, int count)
    {
        byte[] data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short value = (short)(start + i + 10);
            data[i * 2] = (byte)((value >> 8) & 0xFF);
            data[i * 2 + 1] = (byte)(value & 0xFF);
        }

        return data;
    }
}
