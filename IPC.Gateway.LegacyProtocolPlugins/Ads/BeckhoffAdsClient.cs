using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using TwinCAT.Ads;

namespace IPC.Plc.Communication.Ads
{
    public sealed class BeckhoffAdsClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _connection;
        private readonly AdsDriverOptions _options;
        private readonly Dictionary<string, uint> _handles = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _handleLock = new SemaphoreSlim(1, 1);
        private AdsClient _client;
        private bool _targetReachable;
        private volatile bool _handlesInvalidated;

        public BeckhoffAdsClient(PlcConnectionOptions connection)
        {
            _connection = connection ?? new PlcConnectionOptions();
            _options = AdsDriverOptions.Parse(_connection);
        }

        public bool IsConnected => _targetReachable && _client != null && _client.IsConnected;
        public PlcProtocol Protocol => PlcProtocol.BeckhoffAds;

        public void Connect()
        {
            ConnectAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
                return;
            if (string.IsNullOrWhiteSpace(_options.AmsNetId))
                throw new InvalidOperationException("请配置目标 AMS NetId，例如 192.168.1.20.1.1。");

            DisposeClient();
            AdsClient client = new AdsClient { Timeout = Math.Max(100, _connection.TimeoutMilliseconds) };
            try
            {
                await client.ConnectAsync(AmsNetId.Parse(_options.AmsNetId), _options.AdsPort, cancellationToken).ConfigureAwait(false);
                ResultReadDeviceState state = await client.ReadStateAsync(cancellationToken).ConfigureAwait(false);
                if (!state.Succeeded)
                    throw AdsFailureClassifier.Create(state.ErrorCode, "读取 ADS 设备状态");
                client.AdsSymbolVersionChanged += OnAdsSymbolVersionChanged;
                _client = client;
                _targetReachable = true;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public void Disconnect()
        {
            ReleaseHandles();
            DisposeClient();
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            return ReadAsync(address, dataType, elementCount, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            string instancePath = AdsAddress.WithElementOffset(address, elementOffset);
            uint handle = await GetHandleAsync(instancePath, cancellationToken).ConfigureAwait(false);
            Type type = AdsDataCodec.GetManagedType(dataType);
            int[] args = AdsDataCodec.GetMarshalArguments(dataType, elementCount, _options.StringLength);
            ResultAnyValue result = await _client.ReadAnyAsync(handle, type, args, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                throw AdsFailureClassifier.Create(result.ErrorCode, "读取 ADS 标签 " + instancePath);
            return new PlcReadResult(0, dataType.ToString(), result.Value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            return ReadManyAsync(requests, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (requests == null || requests.Count == 0)
                return new List<PlcBatchReadResult>();
            EnsureConnected();
            return await AdsBatchReadExecutor.ReadManyAsync(
                requests,
                _client,
                GetHandleForRequestAsync,
                ReadRequestAsync,
                _options.MaxBatchItems,
                cancellationToken).ConfigureAwait(false);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            WriteAsync(address, dataType, valueText, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            string instancePath = AdsAddress.WithElementOffset(address, elementOffset);
            uint handle = await GetHandleAsync(instancePath, cancellationToken).ConfigureAwait(false);
            object value = AdsDataCodec.ParseWriteValue(dataType, valueText);
            int[] args = AdsDataCodec.GetMarshalArguments(dataType, 1, _options.StringLength);
            ResultWrite result = await _client.WriteAnyAsync(handle, value, args, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                throw AdsFailureClassifier.Create(result.ErrorCode, "写入 ADS 标签 " + instancePath);
        }

        public void Dispose()
        {
            Disconnect();
            _handleLock.Dispose();
        }

        private async ValueTask<uint> GetHandleAsync(string instancePath, CancellationToken cancellationToken)
        {
            await _handleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_handlesInvalidated)
                {
                    _handles.Clear();
                    _handlesInvalidated = false;
                }
                if (_handles.TryGetValue(instancePath, out uint existing))
                    return existing;
                ResultHandle result = await _client.CreateVariableHandleAsync(instancePath, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                    throw AdsFailureClassifier.Create(result.ErrorCode, "解析 ADS 标签 " + instancePath);
                _handles[instancePath] = result.Handle;
                return result.Handle;
            }
            finally
            {
                _handleLock.Release();
            }
        }

        private ValueTask<uint> GetHandleForRequestAsync(PlcBatchReadRequest request, CancellationToken cancellationToken)
        {
            return GetHandleAsync(AdsAddress.WithElementOffset(request.Address, request.ElementOffset), cancellationToken);
        }

        private ValueTask<PlcReadResult> ReadRequestAsync(PlcBatchReadRequest request, CancellationToken cancellationToken)
        {
            return ReadAsync(request.Address, request.DataType, request.ElementCount, request.ElementOffset, cancellationToken);
        }

        private void ReleaseHandles()
        {
            if (_client != null)
            {
                foreach (uint handle in _handles.Values)
                {
                    try { _client.TryDeleteVariableHandle(handle); }
                    catch { }
                }
            }
            _handles.Clear();
            _handlesInvalidated = false;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new PlcCommunicationException("ADS 目标尚未连接或设备状态探测失败。");
        }

        private void DisposeClient()
        {
            _targetReachable = false;
            if (_client == null)
                return;
            _client.AdsSymbolVersionChanged -= OnAdsSymbolVersionChanged;
            try { _client.Disconnect(); }
            catch { }
            _client.Dispose();
            _client = null;
        }

        private void OnAdsSymbolVersionChanged(object sender, AdsSymbolVersionChangedEventArgs args)
        {
            _handlesInvalidated = true;
        }
    }
}
