using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal static class CipArrayReadPlan
    {
        // A Logix Read Tag can return several consecutive elements of the same array.
        // The common acquisition plan already orders tags by symbol and numeric index.
        internal static async ValueTask<IList<PlcBatchReadResult>> ExecuteAsync(
            IList<PlcBatchReadRequest> requests, int maxBytes,
            Func<IList<PlcBatchReadRequest>, ValueTask<IList<PlcBatchReadResult>>> read,
            CancellationToken token)
        {
            if (requests == null || requests.Count < 2)
                return await read(requests).ConfigureAwait(false);

            List<PlcBatchReadRequest> physical = null;
            List<int> counts = null;
            for (int i = 0; i < requests.Count;)
            {
                var first = requests[i];
                int count = 1;
                if (TryGetElement(first, out string symbol, out int start, out PlcDataType arrayType))
                {
                    int bytes = Math.Min(400, maxBytes > 0 ? maxBytes : 400);
                    int limit = (bytes - 16) / PlcDataTypeHelper.GetElementSize(arrayType);
                    while (count < limit && count < requests.Count - i &&
                        TryGetElement(requests[i + count], out string nextSymbol, out int next, out var nextType) &&
                        arrayType == nextType && (long)start + count == next &&
                        string.Equals(symbol, nextSymbol, StringComparison.OrdinalIgnoreCase))
                        count++;
                    if (count > 1)
                    {
                        if (physical == null)
                        {
                            physical = new List<PlcBatchReadRequest>();
                            counts = new List<int>();
                            for (int j = 0; j < i; j++) { physical.Add(requests[j]); counts.Add(1); }
                        }
                        // The starting address already includes its subscript. Clear the logical offset.
                        string address = first.ElementOffset == 0 ? first.Address :
                            symbol + "[" + start.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                        physical.Add(new PlcBatchReadRequest(address, arrayType, count, 0));
                    }
                }
                if (physical != null)
                {
                    if (count == 1) physical.Add(first);
                    counts.Add(count);
                }
                i += count;
            }
            if (physical == null) return await read(requests).ConfigureAwait(false);

            var replies = await read(physical).ConfigureAwait(false);
            var results = new PlcBatchReadResult[requests.Count];
            var retry = new List<PlcBatchReadRequest>();
            var retryIndexes = new List<int>();
            // An MSP may itself be rejected and fall back to individual services.
            // Bound the number of elements retried in this acquisition turn.
            const int maxFallbackElements = 4;
            bool interrupted = token.IsCancellationRequested;
            foreach (var reply in replies) interrupted |= reply.IsDeferred || reply.IsCommunicationError;
            int index = 0;
            for (int i = 0; i < replies.Count; i++)
            {
                var reply = replies[i];
                int count = counts[i];
                if (count == 1) results[index] = reply;
                // The packed BOOL path also accepts DWORD as DINT[]. Do not apply that
                // exception to ordinary scalar DINT tags which require an exact type.
                else if (reply.Success && reply.Result.Value is Array values && values.Length == count &&
                    reply.Result.TypeCode == CipTypeCodes.FromPlcDataType(requests[index].DataType))
                {
                    for (int j = 0; j < count; j++)
                        results[index + j] = PlcBatchReadResult.FromSuccess(requests[index + j],
                            new PlcReadResult(reply.Result.TypeCode, reply.Result.TypeName, values.GetValue(j))
                            { SourceTimestampUtc = reply.Result.SourceTimestampUtc, IsUncertain = reply.Result.IsUncertain });
                }
                else
                {
                    for (int j = index; j < index + count; j++)
                    {
                        if (reply.IsCommunicationError)
                            results[j] = PlcBatchReadResult.FromFailure(requests[j], reply.ErrorMessage, reply.FailureScope);
                        else if (interrupted || retry.Count >= maxFallbackElements)
                            results[j] = PlcBatchReadResult.FromDeferred(requests[j], "CIP array element awaits independent verification.");
                        else
                        {
                            retry.Add(requests[j]);
                            retryIndexes.Add(j);
                        }
                    }
                }
                index += count;
            }
            // A range may cross the end of an array. Use ordinary independent MSP
            // items once to isolate that failure without losing its valid prefix.
            if (retry.Count > 0)
            {
                var individual = await read(retry).ConfigureAwait(false);
                for (int i = 0; i < individual.Count; i++) results[retryIndexes[i]] = individual[i];
            }
            return results;
        }

        private static bool TryGetElement(PlcBatchReadRequest request, out string symbol, out int start, out PlcDataType arrayType)
        {
            symbol = null;
            start = 0;
            arrayType = default;
            if (request == null || request.ElementCount != 1 || request.ElementOffset < 0 ||
                CipExplicitAddress.IsExplicit(request.Address)) return false;
            switch (request.DataType)
            {
                case PlcDataType.Int8: arrayType = PlcDataType.Int8Array; break;
                case PlcDataType.Byte: arrayType = PlcDataType.ByteArray; break;
                case PlcDataType.UInt8: arrayType = PlcDataType.UInt8Array; break;
                case PlcDataType.Int16: arrayType = PlcDataType.Int16Array; break;
                case PlcDataType.UInt16: arrayType = PlcDataType.UInt16Array; break;
                case PlcDataType.Int32: arrayType = PlcDataType.Int32Array; break;
                case PlcDataType.UInt32: arrayType = PlcDataType.UInt32Array; break;
                case PlcDataType.Int64: arrayType = PlcDataType.Int64Array; break;
                case PlcDataType.UInt64: arrayType = PlcDataType.UInt64Array; break;
                case PlcDataType.Float: arrayType = PlcDataType.FloatArray; break;
                case PlcDataType.Double: arrayType = PlcDataType.DoubleArray; break;
                default: return false;
            }
            if (!CipBatchReadItem.TrySplitTrailingSingleIndex(request.Address, out symbol, out start) && request.ElementOffset == 0)
                return false;
            long index = (long)start + request.ElementOffset;
            if (index > int.MaxValue) return false;
            start = (int)index;
            return true;
        }
    }
}
