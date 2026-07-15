using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering
{
    internal static class MeterBatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MeterBatchReadContext<TAddress> context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            PlcBatchReadResult[] ordered = new PlcBatchReadResult[requests.Count];
            Dictionary<string, RawReadState> rawReads = new Dictionary<string, RawReadState>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> countedTimeoutKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int consecutiveMeterTimeouts = 0;

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    TAddress address = context.ParseAddress(request.Address);
                    string key = context.GetAddressKey(address);
                    RawReadState state = GetOrReadRawData(rawReads, key, address, context);
                    PlcReadFailureScope scope = GetMeterFailureScope(state, key, countedTimeoutKeys, ref consecutiveMeterTimeouts);
                    ordered[i] = BuildResult(request, address, state, context, scope);
                }
                catch (Exception ex)
                {
                    if (IsCommunicationException(ex))
                        throw;
                    ordered[i] = PlcBatchReadResult.FromFailure(request, ex.Message, IsCommunicationException(ex));
                }
            }

            for (int i = 0; i < requests.Count; i++)
                output.Add(ordered[i] ?? PlcBatchReadResult.FromFailure(EnsureRequest(requests[i]), "Batch read did not produce a result.", true));

            return output;
        }

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MeterAsyncBatchReadContext<TAddress> context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            PlcBatchReadResult[] ordered = new PlcBatchReadResult[requests.Count];
            Dictionary<string, RawReadState> rawReads = new Dictionary<string, RawReadState>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> countedTimeoutKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int consecutiveMeterTimeouts = 0;

            for (int i = 0; i < requests.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    TAddress address = context.ParseAddress(request.Address);
                    string key = context.GetAddressKey(address);
                    RawReadState state = await GetOrReadRawDataAsync(rawReads, key, address, context, cancellationToken).ConfigureAwait(false);
                    PlcReadFailureScope scope = GetMeterFailureScope(state, key, countedTimeoutKeys, ref consecutiveMeterTimeouts);
                    ordered[i] = BuildResult(request, address, state, context, scope);
                }
                catch (Exception ex)
                {
                    if (IsCommunicationException(ex))
                        throw;
                    ordered[i] = PlcBatchReadResult.FromFailure(request, ex.Message, IsCommunicationException(ex));
                }
            }

            for (int i = 0; i < requests.Count; i++)
                output.Add(ordered[i] ?? PlcBatchReadResult.FromFailure(EnsureRequest(requests[i]), "Batch read did not produce a result.", true));

            return output;
        }

        private static RawReadState GetOrReadRawData<TAddress>(
            Dictionary<string, RawReadState> rawReads,
            string key,
            TAddress address,
            MeterBatchReadContext<TAddress> context)
        {
            if (rawReads.TryGetValue(key, out RawReadState? existing))
                return existing;

            RawReadState state;
            try
            {
                state = RawReadState.FromSuccess(context.ReadRawData(address));
            }
            catch (Exception ex)
            {
                state = RawReadState.FromFailure(ex);
            }

            rawReads[key] = state;
            return state;
        }

        private static async ValueTask<RawReadState> GetOrReadRawDataAsync<TAddress>(
            Dictionary<string, RawReadState> rawReads,
            string key,
            TAddress address,
            MeterAsyncBatchReadContext<TAddress> context,
            CancellationToken cancellationToken)
        {
            if (rawReads.TryGetValue(key, out RawReadState? existing))
                return existing;

            RawReadState state;
            try
            {
                state = RawReadState.FromSuccess(await context.ReadRawDataAsync(address, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                state = RawReadState.FromFailure(ex);
            }

            rawReads[key] = state;
            return state;
        }

        private static PlcBatchReadResult BuildResult<TAddress>(
            PlcBatchReadRequest request,
            TAddress address,
            RawReadState state,
            MeterBatchReadContext<TAddress> context,
            PlcReadFailureScope failureScope)
        {
            if (!state.Success)
                return PlcBatchReadResult.FromFailure(request, state.ErrorMessage, failureScope);

            object value = context.DecodeValue(address, state.Data, request.DataType);
            PlcReadResult result = new PlcReadResult(context.TypeCode, context.TypeName, value);
            return PlcBatchReadResult.FromSuccess(request, result);
        }

        private static PlcBatchReadResult BuildResult<TAddress>(
            PlcBatchReadRequest request,
            TAddress address,
            RawReadState state,
            MeterAsyncBatchReadContext<TAddress> context,
            PlcReadFailureScope failureScope)
        {
            if (!state.Success)
                return PlcBatchReadResult.FromFailure(request, state.ErrorMessage, failureScope);

            object value = context.DecodeValue(address, state.Data, request.DataType);
            PlcReadResult result = new PlcReadResult(context.TypeCode, context.TypeName, value);
            return PlcBatchReadResult.FromSuccess(request, result);
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception? current = exception;
            while (current != null)
            {
                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static PlcReadFailureScope GetMeterFailureScope(
            RawReadState state,
            string key,
            HashSet<string> countedTimeoutKeys,
            ref int consecutiveMeterTimeouts)
        {
            if (state.Success)
            {
                consecutiveMeterTimeouts = 0;
                return PlcReadFailureScope.None;
            }

            if (!state.IsTimeout)
                return state.FailureScope;

            // A silent meter is isolated first. Three distinct silent meter addresses in a row
            // indicate that the transparent gateway/channel itself is probably unavailable.
            if (countedTimeoutKeys.Add(key))
                consecutiveMeterTimeouts++;
            return consecutiveMeterTimeouts >= 3
                ? PlcReadFailureScope.Transport
                : PlcReadFailureScope.Tag;
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest? request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private sealed class RawReadState
        {
            private RawReadState(byte[] data, string errorMessage, PlcReadFailureScope failureScope, bool isTimeout)
            {
                Data = data;
                ErrorMessage = errorMessage;
                FailureScope = failureScope;
                IsTimeout = isTimeout;
            }

            public byte[] Data { get; private set; }
            public string ErrorMessage { get; private set; }
            public PlcReadFailureScope FailureScope { get; private set; }
            public bool IsTimeout { get; private set; }

            public bool Success
            {
                get { return string.IsNullOrEmpty(ErrorMessage); }
            }

            public static RawReadState FromSuccess(byte[] data)
            {
                return new RawReadState(data ?? Array.Empty<byte>(), string.Empty, PlcReadFailureScope.None, false);
            }

            public static RawReadState FromFailure(Exception exception)
            {
                string message = exception == null ? string.Empty : exception.Message;
                bool timeout = exception is TimeoutException ||
                    (exception?.InnerException is TimeoutException);
                PlcReadFailureScope scope = timeout
                    ? PlcReadFailureScope.Tag
                    : PlcFailureClassifier.Classify(exception, PlcReadFailureScope.Tag);
                return new RawReadState(Array.Empty<byte>(), message, scope, timeout);
            }
        }
    }
}
