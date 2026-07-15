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
            foreach (List<PcccBatchReadItem> group in groups.Values)
            {
                group.Sort((left, right) => left.StartElement.CompareTo(right.StartElement));
                ExecuteGroup(group, readRaw, results);
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
            foreach (List<PcccBatchReadItem> group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                group.Sort((left, right) => left.StartElement.CompareTo(right.StartElement));
                await ExecuteGroupAsync(group, readRaw, results, cancellationToken).ConfigureAwait(false);
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
            PlcBatchReadResult[] results)
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
                ExecuteSegment(group, start, index, startElement, endBytes, readRaw, results);
            }
        }

        private static async ValueTask ExecuteGroupAsync(
            List<PcccBatchReadItem> group,
            Func<PcccAddress, int, CancellationToken, ValueTask<byte[]>> readRaw,
            PlcBatchReadResult[] results,
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
                await ExecuteSegmentAsync(group, start, index, startElement, endBytes, readRaw, results, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ExecuteSegment(
            List<PcccBatchReadItem> items,
            int start,
            int end,
            int startElement,
            int byteCount,
            Func<PcccAddress, int, byte[]> readRaw,
            PlcBatchReadResult[] results)
        {
            try
            {
                byte[] data = readRaw(items[start].Address, byteCount);
                DecodeSegment(items, start, end, startElement, data, results);
            }
            catch (Exception ex)
            {
                HandleFailure(items, start, end, ex, results,
                    (left, right) => ExecuteSegment(items, left, right, items[left].StartElement,
                        GetByteCount(items, left, right), readRaw, results));
            }
        }

        private static async ValueTask ExecuteSegmentAsync(
            List<PcccBatchReadItem> items,
            int start,
            int end,
            int startElement,
            int byteCount,
            Func<PcccAddress, int, CancellationToken, ValueTask<byte[]>> readRaw,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await readRaw(items[start].Address, byteCount, cancellationToken).ConfigureAwait(false);
                DecodeSegment(items, start, end, startElement, data, results);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                if (scope != PlcReadFailureScope.Tag || end - start <= 1)
                {
                    SetFailures(items, start, end, ex.Message, scope, results);
                    return;
                }
                int middle = start + (end - start) / 2;
                await ExecuteSegmentAsync(items, start, middle, items[start].StartElement,
                    GetByteCount(items, start, middle), readRaw, results, cancellationToken).ConfigureAwait(false);
                await ExecuteSegmentAsync(items, middle, end, items[middle].StartElement,
                    GetByteCount(items, middle, end), readRaw, results, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void HandleFailure(
            List<PcccBatchReadItem> items,
            int start,
            int end,
            Exception exception,
            PlcBatchReadResult[] results,
            Action<int, int> split)
        {
            PlcReadFailureScope scope = PlcFailureClassifier.Classify(exception, PlcReadFailureScope.Tag);
            if (scope == PlcReadFailureScope.Tag && end - start > 1)
            {
                int middle = start + (end - start) / 2;
                split(start, middle);
                split(middle, end);
                return;
            }
            SetFailures(items, start, end, exception.Message, scope, results);
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
                output.Add(results[index] ?? PlcBatchReadResult.FromFailure(request, "PCCC批读未返回结果。", PlcReadFailureScope.Batch));
            }
            return output;
        }
    }
}
