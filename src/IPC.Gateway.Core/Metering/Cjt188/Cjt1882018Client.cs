#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering.Cjt188
{
    /// <summary>CJ/T 188-2018 普通明文读数据客户端。</summary>
    public sealed class Cjt1882018Client : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly Cjt188Client _inner;

        public Cjt1882018Client(PlcConnectionOptions options)
        {
            _inner = new Cjt188Client(options ?? throw new ArgumentNullException(nameof(options)));
        }

        public bool IsConnected => _inner.IsConnected;
        public PlcProtocol Protocol => PlcProtocol.Cjt1882018;
        public void Connect() => _inner.Connect();
        public ValueTask ConnectAsync(CancellationToken cancellationToken) => _inner.ConnectAsync(cancellationToken);
        public void Disconnect() => _inner.Disconnect();
        public ValueTask DisconnectAsync(CancellationToken cancellationToken) => _inner.DisconnectAsync(cancellationToken);

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            PlcReadResult result = _inner.Read(NormalizeAddress(address), dataType, elementCount, elementOffset);
            return ConvertResult(result);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            PlcReadResult result = await _inner.ReadAsync(
                NormalizeAddress(address), dataType, elementCount, elementOffset, cancellationToken).ConfigureAwait(false);
            return ConvertResult(result);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            IList<PlcBatchReadRequest> normalized = NormalizeRequests(requests);
            return RemapResults(requests, _inner.ReadMany(normalized));
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            IList<PlcBatchReadRequest> normalized = NormalizeRequests(requests);
            IList<PlcBatchReadResult> results = await _inner.ReadManyAsync(normalized, cancellationToken).ConfigureAwait(false);
            return RemapResults(requests, results);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset) =>
            throw new NotSupportedException("CJ/T188-2018 当前按表计安全策略仅开放读数据；参数设置和控制操作未开放。");

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new NotSupportedException("CJ/T188-2018 当前按表计安全策略仅开放读数据。"));

        public void Dispose() => _inner.Dispose();

        private static PlcReadResult ConvertResult(PlcReadResult result) =>
            new PlcReadResult(0x1888, "CJ/T188-2018", result.Value);

        internal static string NormalizeAddress(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            const string versionPrefix = "CJ188-2018:";
            return normalized.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase)
                ? "CJ188:" + normalized.Substring(versionPrefix.Length)
                : normalized;
        }

        private static IList<PlcBatchReadRequest> NormalizeRequests(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            List<PlcBatchReadRequest> result = new List<PlcBatchReadRequest>(requests.Count);
            foreach (PlcBatchReadRequest request in requests)
            {
                result.Add(new PlcBatchReadRequest(
                    NormalizeAddress(request.Address),
                    request.DataType,
                    request.ElementCount,
                    request.ElementOffset));
            }
            return result;
        }

        private static IList<PlcBatchReadResult> RemapResults(
            IList<PlcBatchReadRequest> requests,
            IList<PlcBatchReadResult> results)
        {
            List<PlcBatchReadResult> remapped = new List<PlcBatchReadResult>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                PlcBatchReadResult result = results[i];
                remapped.Add(result.Success
                    ? PlcBatchReadResult.FromSuccess(requests[i], ConvertResult(result.Result!))
                    : PlcBatchReadResult.FromFailure(requests[i], result.ErrorMessage, result.FailureScope));
            }
            return remapped;
        }
    }
}
