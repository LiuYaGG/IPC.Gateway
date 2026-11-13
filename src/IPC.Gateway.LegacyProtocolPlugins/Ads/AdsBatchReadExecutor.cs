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
            maxBatchItems = Math.Max(1, maxBatchItems);
            List<int> scalarIndexes = new List<int>();
            try
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    if (PlcDataTypeHelper.IsArray(requests[i].DataType) || requests[i].DataType == PlcDataType.String)
                    {
                        try
                        {
                            PlcReadResult value = await readSingle(requests[i], cancellationToken).ConfigureAwait(false);
                            ordered[i] = PlcBatchReadResult.FromSuccess(requests[i], value);
                        }
                        catch (Exception ex)
                        {
                            var scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                            ordered[i] = PlcBatchReadResult.FromFailure(requests[i], ex.Message, scope);
                            if (PlcBatchReadResult.IsConnectionFailureScope(scope)) throw;
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
            }
            catch (Exception ex)
            {
                PlcBatchReadResult.DeferPending(requests, ordered, ex.Message);
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
                    Type type = AdsDataCodec.GetManagedType(request.DataType);
                    uint handle = await getHandle(request, cancellationToken).ConfigureAwait(false);
                    handles.Add(handle);
                    types.Add(type);
                    validIndexes.Add(requestIndex);
                }
                catch (Exception ex)
                {
                    var scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                    ordered[requestIndex] = PlcBatchReadResult.FromFailure(request, ex.Message, scope);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope)) throw;
                }
            }

            if (handles.Count == 0)
                return;

            SumHandleReadAnyType command = new SumHandleReadAnyType(client, handles.ToArray(), types.ToArray());
            ResultSumValues result;
            try
            {
                result = await command.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded && result.ErrorCode != AdsErrorCode.NoError)
                    throw AdsFailureClassifier.Create(result.ErrorCode, "执行 ADS Sum Read");
            }
            catch (Exception error)
            {
                var scope = PlcFailureClassifier.Classify(error, PlcReadFailureScope.Batch);
                foreach (int requestIndex in validIndexes)
                    ordered[requestIndex] = PlcBatchReadResult.FromFailure(requests[requestIndex], error.Message, scope);
                throw;
            }

            Exception connectionError = ApplyScalarResults(requests, validIndexes, ordered, result.SubErrors, result.Values);
            if (connectionError != null) throw connectionError;
        }

        internal static Exception ApplyScalarResults(
            IList<PlcBatchReadRequest> requests, IList<int> validIndexes, PlcBatchReadResult[] ordered,
            IReadOnlyList<AdsErrorCode> subErrors, IReadOnlyList<object> values)
        {
            if (subErrors == null || values == null || subErrors.Count != validIndexes.Count || values.Count != validIndexes.Count)
            {
                var error = new PlcProtocolException(PlcReadFailureScope.Session, "ADS Sum Read response item count is invalid.");
                foreach (int requestIndex in validIndexes)
                    ordered[requestIndex] ??= PlcBatchReadResult.FromFailure(requests[requestIndex], error.Message, error.FailureScope);
                return error;
            }
            Exception connectionError = null;
            for (int i = 0; i < validIndexes.Count; i++)
            {
                int requestIndex = validIndexes[i];
                PlcBatchReadRequest request = requests[requestIndex];
                AdsErrorCode subError = subErrors[i];
                if (subError == AdsErrorCode.NoError)
                {
                    ordered[requestIndex] = PlcBatchReadResult.FromSuccess(
                        request,
                        new PlcReadResult(0, request.DataType.ToString(), values[i]));
                    continue;
                }

                Exception error = AdsFailureClassifier.Create(subError, "读取 ADS 标签 " + request.Address);
                var failureScope = PlcFailureClassifier.Classify(error, PlcReadFailureScope.Batch);
                ordered[requestIndex] = PlcBatchReadResult.FromFailure(request, error.Message, failureScope);
                if (PlcBatchReadResult.IsConnectionFailureScope(failureScope))
                    connectionError = connectionError ?? error;
            }
            return connectionError;
        }

        private static bool IsTagError(Exception exception)
        {
            return exception is AdsTagException || exception is ArgumentException || exception is FormatException || exception is NotSupportedException;
        }
    }
}
