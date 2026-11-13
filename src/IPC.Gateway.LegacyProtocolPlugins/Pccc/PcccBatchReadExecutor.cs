using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Pccc
{
    internal static class PcccBatchReadExecutor
    {
        private const int MaxPayloadBytes = 220;
        private const int MaxGapElements = 2;

        public static IList<PlcBatchReadResult> ReadMany(
            IList<PlcBatchReadRequest> requests,
            Func<PcccAddress, int, byte[]> readRaw)
        {
            PlcBatchReadResult[] results = new PlcBatchReadResult[requests?.Count ?? 0];
            Dictionary<string, List<PcccBatchReadItem>> groups = BuildGroups(requests, results);
            var recovery = PlcBatchReadRecovery.Start(readRaw.Target, "pccc");
            try
            {
                foreach (List<PcccBatchReadItem> group in groups.Values)
                {
                    group.Sort((left, right) => left.StartElement.CompareTo(right.StartElement));
                    ExecuteGroup(group, readRaw, results, recovery);
                }
            }
            catch (Exception ex)
            {
                PlcBatchReadResult.DeferPending(requests, results, ex.Message);
            }
            return Complete(requests, results);
        }

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            Func<PcccAddress, int, CancellationToken, ValueTask<byte[]>> readRaw,
            CancellationToken cancellationToken)
        {
            PlcBatchReadResult[] results = new PlcBatchReadResult[requests?.Count ?? 0];
            Dictionary<string, List<PcccBatchReadItem>> groups = BuildGroups(requests, results);
            var recovery = PlcBatchReadRecovery.Start(readRaw.Target, "pccc");
            try
            {
                foreach (List<PcccBatchReadItem> group in groups.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    group.Sort((left, right) => left.StartElement.CompareTo(right.StartElement));
                    await ExecuteGroupAsync(group, readRaw, results, recovery, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                PlcBatchReadResult.DeferPending(requests, results, ex.Message);
            }
            return Complete(requests, results);
        }

        private static Dictionary<string, List<PcccBatchReadItem>> BuildGroups(
            IList<PlcBatchReadRequest> requests,
            PlcBatchReadResult[] results)
        {
            Dictionary<string, List<PcccBatchReadItem>> groups =
                new Dictionary<string, List<PcccBatchReadItem>>(StringComparer.Ordinal);
            if (requests == null)
                return groups;

            for (int index = 0; index < requests.Count; index++)
            {
                PlcBatchReadRequest request = requests[index] ??
                    new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                try
                {
                    PcccBatchReadItem item = PcccBatchReadItem.Create(index, request);
                    if (!groups.TryGetValue(item.GroupKey, out List<PcccBatchReadItem> group))
                    {
                        group = new List<PcccBatchReadItem>();
                        groups.Add(item.GroupKey, group);
                    }
                    group.Add(item);
                }
                catch (Exception ex)
                {
                    results[index] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
            return groups;
        }

        private static void ExecuteGroup(
            List<PcccBatchReadItem> group,
            Func<PcccAddress, int, byte[]> readRaw,
            PlcBatchReadResult[] results, PlcBatchReadRecovery recovery)
        {
            int index = 0;
            while (index < group.Count)
            {
                int start = index;
                int startElement = group[index].StartElement;
                int endBytes = group[index].GetEndByteOffset(startElement);
                index++;
                while (index < group.Count)
                {
                    PcccBatchReadItem next = group[index];
                    int gap = next.StartElement - group[index - 1].StartElement;
                    int mergedBytes = next.GetEndByteOffset(startElement);
                    if (gap > MaxGapElements + 1 || mergedBytes > MaxPayloadBytes)
                        break;
                    endBytes = Math.Max(endBytes, mergedBytes);
                    index++;
                }
                recovery.ReadRange(group, start, index, item => item.Request, item => item.Index,
                    (left, right) =>
                    {
                        byte[] data = readRaw(group[left].Address, GetByteCount(group, left, right));
                        DecodeSegment(group, left, right, group[left].StartElement, data, results);
                    }, results, group[start].GroupKey);
            }
        }

        private static async ValueTask ExecuteGroupAsync(
            List<PcccBatchReadItem> group,
            Func<PcccAddress, int, CancellationToken, ValueTask<byte[]>> readRaw,
            PlcBatchReadResult[] results, PlcBatchReadRecovery recovery,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < group.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start = index;
                int startElement = group[index].StartElement;
                int endBytes = group[index].GetEndByteOffset(startElement);
                index++;
                while (index < group.Count)
                {
                    PcccBatchReadItem next = group[index];
                    int gap = next.StartElement - group[index - 1].StartElement;
                    int mergedBytes = next.GetEndByteOffset(startElement);
                    if (gap > MaxGapElements + 1 || mergedBytes > MaxPayloadBytes)
                        break;
                    endBytes = Math.Max(endBytes, mergedBytes);
                    index++;
                }
                await recovery.ReadRangeAsync(group, start, index, item => item.Request, item => item.Index,
                    async (left, right) =>
                    {
                        byte[] data = await readRaw(group[left].Address, GetByteCount(group, left, right), cancellationToken).ConfigureAwait(false);
                        DecodeSegment(group, left, right, group[left].StartElement, data, results);
                    }, results, group[start].GroupKey, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void DecodeSegment(
            List<PcccBatchReadItem> items,
            int start,
            int end,
            int startElement,
            byte[] data,
            PlcBatchReadResult[] results)
        {
            for (int index = start; index < end; index++)
            {
                PcccBatchReadItem item = items[index];
                int offset = (item.StartElement - startElement) * item.Address.NativeElementSize;
                byte[] value = new byte[item.ByteCount];
                Buffer.BlockCopy(data, offset, value, 0, value.Length);
                object decoded = PcccDataCodec.Decode(item.Address, item.Request.DataType, value, item.Request.ElementCount);
                results[item.Index] = PlcBatchReadResult.FromSuccess(
                    item.Request,
                    new PlcReadResult(item.Address.FileTypeCode, item.Address.FileTypeName, decoded));
            }
        }

        private static int GetByteCount(List<PcccBatchReadItem> items, int start, int end)
        {
            int startElement = items[start].StartElement;
            int byteCount = 0;
            for (int index = start; index < end; index++)
                byteCount = Math.Max(byteCount, items[index].GetEndByteOffset(startElement));
            return byteCount;
        }

        private static void SetFailures(List<PcccBatchReadItem> items, int start, int end, string message,
            PlcReadFailureScope scope, PlcBatchReadResult[] results)
        {
            for (int index = start; index < end; index++)
                results[items[index].Index] = PlcBatchReadResult.FromFailure(items[index].Request, message, scope);
        }

        private static IList<PlcBatchReadResult> Complete(IList<PlcBatchReadRequest> requests, PlcBatchReadResult[] results)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>(results.Length);
            for (int index = 0; index < results.Length; index++)
            {
                PlcBatchReadRequest request = requests[index] ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                output.Add(results[index] ?? PlcBatchReadResult.FromDeferred(request, "PCCC batch read was not attempted."));
            }
            return output;
        }
    }
}
