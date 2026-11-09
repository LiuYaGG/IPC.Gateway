using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;

namespace IPC.Plc.Communication.Ads
{
    internal static class AdsBatchReadExecutor
    {
        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            AdsClient client,
            Func<PlcBatchReadRequest, CancellationToken, ValueTask<uint>> getHandle,
            Func<PlcBatchReadRequest, CancellationToken, ValueTask<PlcReadResult>> readSingle,
            int maxBatchItems,
            CancellationToken cancellationToken)
        {
            PlcBatchReadResult[] ordered = new PlcBatchReadResult[requests.Count];
            List<int> scalarIndexes = new List<int>();
            for (int i = 0; i < requests.Count; i++)
            {
                if (PlcDataTypeHelper.IsArray(requests[i].DataType))
                {
                    try
                    {
                        PlcReadResult value = await readSingle(requests[i], cancellationToken).ConfigureAwait(false);
                        ordered[i] = PlcBatchReadResult.FromSuccess(requests[i], value);
                    }
                    catch (Exception ex) when (IsTagError(ex))
                    {
                        ordered[i] = PlcBatchReadResult.FromFailure(requests[i], ex.Message, PlcReadFailureScope.Tag);
                    }
                }
                else
                {
                    scalarIndexes.Add(i);
                }
            }

            for (int offset = 0; offset < scalarIndexes.Count; offset += maxBatchItems)
            {
                int count = Math.Min(maxBatchItems, scalarIndexes.Count - offset);
                await ReadScalarChunkAsync(
                    requests,
                    scalarIndexes,
                    ordered,
                    offset,
                    count,
                    client,
                    getHandle,
                    cancellationToken).ConfigureAwait(false);
            }

            return ordered;
        }

        private static async ValueTask ReadScalarChunkAsync(
            IList<PlcBatchReadRequest> requests,
            List<int> scalarIndexes,
            PlcBatchReadResult[] ordered,
            int offset,
            int count,
            AdsClient client,
            Func<PlcBatchReadRequest, CancellationToken, ValueTask<uint>> getHandle,
            CancellationToken cancellationToken)
        {
            List<int> validIndexes = new List<int>(count);
            List<uint> handles = new List<uint>(count);
            List<Type> types = new List<Type>(count);
            for (int i = 0; i < count; i++)
            {
                int requestIndex = scalarIndexes[offset + i];
                PlcBatchReadRequest request = requests[requestIndex];
                try
                {
                    handles.Add(await getHandle(request, cancellationToken).ConfigureAwait(false));
                    types.Add(AdsDataCodec.GetManagedType(request.DataType));
                    validIndexes.Add(requestIndex);
                }
                catch (Exception ex) when (IsTagError(ex))
                {
                    ordered[requestIndex] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }

            if (handles.Count == 0)
                return;

            SumHandleReadAnyType command = new SumHandleReadAnyType(client, handles.ToArray(), types.ToArray());
            ResultSumValues result = await command.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                throw AdsFailureClassifier.Create(result.ErrorCode, "执行 ADS Sum Read");

            for (int i = 0; i < validIndexes.Count; i++)
            {
                int requestIndex = validIndexes[i];
                PlcBatchReadRequest request = requests[requestIndex];
                AdsErrorCode subError = result.SubErrors[i];
                if (subError == AdsErrorCode.NoError)
                {
                    ordered[requestIndex] = PlcBatchReadResult.FromSuccess(
                        request,
                        new PlcReadResult(0, request.DataType.ToString(), result.Values[i]));
                    continue;
                }

                Exception error = AdsFailureClassifier.Create(subError, "读取 ADS 标签 " + request.Address);
                if (error is AdsTagException)
                    ordered[requestIndex] = PlcBatchReadResult.FromFailure(request, error.Message, PlcReadFailureScope.Tag);
                else
                    throw error;
            }
        }

        private static bool IsTagError(Exception exception)
        {
            return exception is AdsTagException || exception is FormatException || exception is NotSupportedException;
        }
    }
}
