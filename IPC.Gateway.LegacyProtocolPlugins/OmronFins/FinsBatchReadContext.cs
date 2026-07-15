using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    internal sealed class FinsBatchReadContext
    {
        public Func<byte, int, int, int, int, byte[]> ReadMemory { get; set; }
        public Func<IList<FinsMemoryPoint>, byte[]> ReadMultipleMemory { get; set; }
        public PlcWordOrder WordOrder { get; set; }
        public int MaxWordCount { get; set; }
        public int MaxBitCount { get; set; }
        public int MaxGapWords { get; set; }
        public int MaxSparseItems { get; set; }
        public FinsDriverOptions DriverOptions { get; set; }
    }

    internal sealed class FinsAsyncBatchReadContext
    {
        public Func<byte, int, int, int, int, CancellationToken, ValueTask<byte[]>> ReadMemoryAsync { get; set; }
        public Func<IList<FinsMemoryPoint>, CancellationToken, ValueTask<byte[]>> ReadMultipleMemoryAsync { get; set; }
        public PlcWordOrder WordOrder { get; set; }
        public int MaxWordCount { get; set; }
        public int MaxBitCount { get; set; }
        public int MaxGapWords { get; set; }
        public int MaxSparseItems { get; set; }
        public FinsDriverOptions DriverOptions { get; set; }
    }
}
