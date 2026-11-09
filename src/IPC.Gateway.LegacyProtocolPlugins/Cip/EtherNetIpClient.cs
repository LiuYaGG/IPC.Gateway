#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using Sres.Net.EEIP;

namespace IPC.Plc.Communication.Cip
{
    /// <summary>
    /// 通用 EtherNet/IP 显式消息客户端。与 Rockwell 符号标签驱动分离，
    /// 面向任意实现 CIP Object Model 的设备。
    /// </summary>
    public sealed class EtherNetIpClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private readonly EtherNetIpDriverOptions _driverOptions;
        private readonly CipClient _inner;
        private readonly object _ioSync = new object();
        private EEIPClient? _ioClient;
        private bool _implicitConnected;

        public EtherNetIpClient(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _driverOptions = EtherNetIpDriverOptions.Parse(_options.DriverOptionsJson);
            _inner = new CipClient(_options);
        }

        public bool IsConnected => _driverOptions.UsesImplicitIo ? _implicitConnected : _inner.IsConnected;
        public PlcProtocol Protocol => PlcProtocol.EtherNetIp;

        public void Connect()
        {
            if (!_driverOptions.UsesImplicitIo)
            {
                _inner.Connect();
                return;
            }

            lock (_ioSync)
            {
                if (_implicitConnected)
                    return;

                EEIPClient client = new EEIPClient
                {
                    IPAddress = _options.Host,
                    TCPPort = checked((ushort)(_options.Port > 0 ? _options.Port : 44818)),
                    O_T_InstanceID = _driverOptions.OutputAssembly,
                    T_O_InstanceID = _driverOptions.InputAssembly,
                    ConfigurationAssemblyInstanceID = _driverOptions.ConfigurationAssembly,
                    O_T_Length = _driverOptions.OutputLength,
                    T_O_Length = _driverOptions.InputLength,
                    RequestedPacketRate_O_T = checked((uint)_driverOptions.RequestedPacketIntervalMilliseconds * 1000U),
                    RequestedPacketRate_T_O = checked((uint)_driverOptions.RequestedPacketIntervalMilliseconds * 1000U),
                    O_T_RealTimeFormat = ParseRealTimeFormat(_driverOptions.OutputRealTimeFormat, RealTimeFormat.Header32Bit),
                    T_O_RealTimeFormat = ParseRealTimeFormat(_driverOptions.InputRealTimeFormat, RealTimeFormat.Modeless),
                    O_T_ConnectionType = ConnectionType.Point_to_Point,
                    T_O_ConnectionType = ParseConnectionType(_driverOptions.InputConnectionType),
                    O_T_Priority = Priority.Scheduled,
                    T_O_Priority = Priority.Scheduled
                };

                try
                {
                    client.RegisterSession();
                    if (client.O_T_Length == 0)
                        client.O_T_Length = client.Detect_O_T_Length();
                    if (client.T_O_Length == 0)
                        client.T_O_Length = client.Detect_T_O_Length();
                    client.ForwardOpen(client.O_T_Length > 511 || client.T_O_Length > 511);
                    _ioClient = client;
                    _implicitConnected = true;
                }
                catch
                {
                    SafeClose(client);
                    throw;
                }
            }
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (!_driverOptions.UsesImplicitIo)
            {
                await _inner.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(Connect, cancellationToken).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            _inner.Disconnect();
            lock (_ioSync)
            {
                EEIPClient? client = _ioClient;
                _ioClient = null;
                _implicitConnected = false;
                if (client != null)
                    SafeClose(client);
            }
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (EtherNetIpIoAddress.IsIoAddress(address))
                return ReadIo(EtherNetIpIoAddress.Parse(address), dataType, elementCount, elementOffset);
            EnsureExplicitConnected();
            return _inner.Read(EtherNetIpAddress.Normalize(address), dataType, elementCount, elementOffset);
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken) =>
            ReadAsyncCore(address, dataType, elementCount, elementOffset, cancellationToken);

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            if (ContainsIoAddress(requests))
                return ReadManyWithIo(requests);
            EnsureExplicitConnected();
            IList<PlcBatchReadRequest> normalized = NormalizeRequests(requests);
            return RemapResults(requests, _inner.ReadMany(normalized));
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (ContainsIoAddress(requests))
                return await ReadManyWithIoAsync(requests, cancellationToken).ConfigureAwait(false);
            await EnsureExplicitConnectedAsync(cancellationToken).ConfigureAwait(false);
            IList<PlcBatchReadRequest> normalized = NormalizeRequests(requests);
            IList<PlcBatchReadResult> results = await _inner.ReadManyAsync(normalized, cancellationToken).ConfigureAwait(false);
            return RemapResults(requests, results);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            if (EtherNetIpIoAddress.IsIoAddress(address))
            {
                WriteIo(EtherNetIpIoAddress.Parse(address), dataType, valueText, elementOffset);
                return;
            }
            EnsureExplicitConnected();
            _inner.Write(EtherNetIpAddress.Normalize(address), dataType, valueText, elementOffset);
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken) =>
            WriteAsyncCore(address, dataType, valueText, elementOffset, cancellationToken);

        public void Dispose()
        {
            Disconnect();
            _inner.Dispose();
        }

        private async ValueTask<PlcReadResult> ReadAsyncCore(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EtherNetIpIoAddress.IsIoAddress(address))
                return ReadIo(EtherNetIpIoAddress.Parse(address), dataType, elementCount, elementOffset);
            await EnsureExplicitConnectedAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.ReadAsync(
                EtherNetIpAddress.Normalize(address),
                dataType,
                elementCount,
                elementOffset,
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteAsyncCore(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EtherNetIpIoAddress.IsIoAddress(address))
            {
                WriteIo(EtherNetIpIoAddress.Parse(address), dataType, valueText, elementOffset);
                return;
            }
            await EnsureExplicitConnectedAsync(cancellationToken).ConfigureAwait(false);
            await _inner.WriteAsync(
                EtherNetIpAddress.Normalize(address),
                dataType,
                valueText,
                elementOffset,
                cancellationToken).ConfigureAwait(false);
        }

        private PlcReadResult ReadIo(
            EtherNetIpIoAddress address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            EnsureImplicitConnected();
            EEIPClient client = _ioClient!;
            if (address.Direction == EtherNetIpIoDirection.Input)
                EnsureInputFresh(client);

            byte[] source = address.Direction == EtherNetIpIoDirection.Input
                ? client.T_O_IOData ?? Array.Empty<byte>()
                : client.O_T_IOData ?? Array.Empty<byte>();
            byte[] snapshot = new byte[source.Length];
            Buffer.BlockCopy(source, 0, snapshot, 0, source.Length);

            int baseOffset = address.Direction == EtherNetIpIoDirection.Input
                ? _driverOptions.InputDataOffset
                : _driverOptions.OutputDataOffset;
            object value = DecodeIoValue(snapshot, baseOffset, address, dataType, elementCount, elementOffset);
            ushort typeCode = CipTypeCodes.FromPlcDataType(dataType);
            return new PlcReadResult(typeCode, CipTypeCodes.ToName(typeCode), value);
        }

        private void WriteIo(EtherNetIpIoAddress address, PlcDataType dataType, string valueText, int elementOffset)
        {
            if (address.Direction != EtherNetIpIoDirection.Output)
                throw new NotSupportedException("EtherNet/IP Input 周期数据为只读；写入请使用 Output 地址。");

            EnsureImplicitConnected();
            lock (_ioSync)
            {
                byte[] target = _ioClient!.O_T_IOData ?? Array.Empty<byte>();
                int baseOffset = checked(_driverOptions.OutputDataOffset + address.ByteOffset);
                if (address.BitOffset.HasValue)
                {
                    if (dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                        throw new NotSupportedException("带位偏移的周期 I/O 写入只支持 Bool 或 BoolArray。");
                    bool[] values = dataType == PlcDataType.Bool
                        ? new[] { ParseBoolean(valueText) }
                        : ParseBooleanArray(valueText);
                    for (int i = 0; i < values.Length; i++)
                    {
                        int bitIndex = checked(address.BitOffset.Value + elementOffset + i);
                        int byteIndex = checked(baseOffset + bitIndex / 8);
                        EnsureRange(target, byteIndex, 1);
                        byte mask = (byte)(1 << (bitIndex % 8));
                        target[byteIndex] = values[i]
                            ? (byte)(target[byteIndex] | mask)
                            : (byte)(target[byteIndex] & ~mask);
                    }
                    return;
                }

                byte[] encoded = EncodeIoValue(dataType, valueText);
                int elementSize = GetIoElementSize(dataType);
                int byteIndexWithOffset = checked(baseOffset + elementOffset * elementSize);
                EnsureRange(target, byteIndexWithOffset, encoded.Length);
                Buffer.BlockCopy(encoded, 0, target, byteIndexWithOffset, encoded.Length);
            }
        }

        private object DecodeIoValue(
            byte[] source,
            int baseOffset,
            EtherNetIpIoAddress address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            int start = checked(baseOffset + address.ByteOffset);
            if (address.BitOffset.HasValue)
            {
                if (dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                    throw new NotSupportedException("带位偏移的周期 I/O 地址只支持 Bool 或 BoolArray。");
                int bitCount = dataType == PlcDataType.BoolArray ? Math.Max(1, elementCount) : 1;
                bool[] values = new bool[bitCount];
                for (int i = 0; i < bitCount; i++)
                {
                    int bitIndex = checked(address.BitOffset.Value + elementOffset + i);
                    int byteIndex = checked(start + bitIndex / 8);
                    EnsureRange(source, byteIndex, 1);
                    values[i] = (source[byteIndex] & (1 << (bitIndex % 8))) != 0;
                }
                return dataType == PlcDataType.Bool ? values[0] : values;
            }

            if (dataType == PlcDataType.String)
            {
                int length = Math.Max(1, elementCount);
                int stringStart = checked(start + elementOffset);
                EnsureRange(source, stringStart, length);
                return System.Text.Encoding.UTF8.GetString(source, stringStart, length).TrimEnd('\0');
            }

            int count = PlcDataTypeHelper.IsArray(dataType) ? Math.Max(1, elementCount) : 1;
            int elementSize = GetIoElementSize(dataType);
            int dataStart = checked(start + elementOffset * elementSize);
            int byteCount = checked(count * elementSize);
            EnsureRange(source, dataStart, byteCount);
            byte[] selected = new byte[byteCount];
            Buffer.BlockCopy(source, dataStart, selected, 0, byteCount);
            return CipDataCodec.Decode(dataType, CipTypeCodes.FromPlcDataType(dataType), selected, count);
        }

        private static byte[] EncodeIoValue(PlcDataType dataType, string valueText)
        {
            if (dataType == PlcDataType.String)
                return System.Text.Encoding.UTF8.GetBytes(valueText ?? string.Empty);
            return CipDataCodec.Encode(dataType, valueText ?? string.Empty);
        }

        private static int GetIoElementSize(PlcDataType dataType)
        {
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray ||
                dataType == PlcDataType.Int8 || dataType == PlcDataType.Int8Array ||
                dataType == PlcDataType.UInt8 || dataType == PlcDataType.UInt8Array)
                return 1;
            return PlcDataTypeHelper.GetElementSize(dataType);
        }

        private static void EnsureRange(byte[] buffer, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length || buffer.Length - offset < count)
                throw new InvalidOperationException("周期 I/O 数据长度不足，请检查 Assembly 长度和数据偏移配置。");
        }

        private static bool ParseBoolean(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool[] ParseBooleanArray(string value)
        {
            string[] items = (value ?? string.Empty).Split(
                new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (items.Length == 0)
                return new[] { false };
            bool[] values = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
                values[i] = ParseBoolean(items[i]);
            return values;
        }

        private void EnsureImplicitConnected()
        {
            if (!_driverOptions.UsesImplicitIo)
                throw new InvalidOperationException("Input/Output 周期地址需要把 EtherNet/IP I/O 模式设置为 Implicit。");
            if (!_implicitConnected)
                Connect();
        }

        private void EnsureExplicitConnected()
        {
            if (!_inner.IsConnected)
                _inner.Connect();
        }

        private async ValueTask EnsureExplicitConnectedAsync(CancellationToken cancellationToken)
        {
            if (!_inner.IsConnected)
                await _inner.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        private void EnsureInputFresh(EEIPClient client)
        {
            DateTime lastReceived = client.LastReceivedImplicitMessage;
            if (lastReceived == default)
                throw new TimeoutException("EtherNet/IP 周期输入尚未收到数据。");
            if (DateTime.UtcNow - lastReceived.ToUniversalTime() >
                TimeSpan.FromMilliseconds(_driverOptions.IoStaleTimeoutMilliseconds))
                throw new TimeoutException("EtherNet/IP 周期输入已超出新鲜度阈值。");
        }

        private static RealTimeFormat ParseRealTimeFormat(string value, RealTimeFormat fallback)
        {
            return Enum.TryParse(value, true, out RealTimeFormat result) ? result : fallback;
        }

        private static ConnectionType ParseConnectionType(string value)
        {
            string normalized = (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            return normalized.Equals("Multicast", StringComparison.OrdinalIgnoreCase)
                ? ConnectionType.Multicast
                : ConnectionType.Point_to_Point;
        }

        private static void SafeClose(EEIPClient client)
        {
            try { client.ForwardClose(); } catch { }
            try { client.UnRegisterSession(); } catch { }
        }

        private static bool ContainsIoAddress(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null)
                return false;
            foreach (PlcBatchReadRequest request in requests)
            {
                if (request != null && EtherNetIpIoAddress.IsIoAddress(request.Address))
                    return true;
            }
            return false;
        }

        private IList<PlcBatchReadResult> ReadManyWithIo(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>(requests.Count);
            foreach (PlcBatchReadRequest request in requests)
            {
                try
                {
                    results.Add(PlcBatchReadResult.FromSuccess(
                        request,
                        Read(request.Address, request.DataType, request.ElementCount, request.ElementOffset)));
                }
                catch (Exception ex)
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                        throw;
                    results.Add(PlcBatchReadResult.FromFailure(request, ex.Message, scope));
                }
            }
            return results;
        }

        private async ValueTask<IList<PlcBatchReadResult>> ReadManyWithIoAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>(requests.Count);
            foreach (PlcBatchReadRequest request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    PlcReadResult result = await ReadAsync(
                        request.Address,
                        request.DataType,
                        request.ElementCount,
                        request.ElementOffset,
                        cancellationToken).ConfigureAwait(false);
                    results.Add(PlcBatchReadResult.FromSuccess(request, result));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                        throw;
                    results.Add(PlcBatchReadResult.FromFailure(request, ex.Message, scope));
                }
            }
            return results;
        }

        private static IList<PlcBatchReadRequest> NormalizeRequests(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            List<PlcBatchReadRequest> normalized = new List<PlcBatchReadRequest>(requests.Count);
            foreach (PlcBatchReadRequest request in requests)
            {
                PlcBatchReadRequest item = request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                normalized.Add(new PlcBatchReadRequest(
                    EtherNetIpAddress.Normalize(item.Address),
                    item.DataType,
                    item.ElementCount,
                    item.ElementOffset));
            }
            return normalized;
        }

        private static IList<PlcBatchReadResult> RemapResults(
            IList<PlcBatchReadRequest> requests,
            IList<PlcBatchReadResult> results)
        {
            List<PlcBatchReadResult> remapped = new List<PlcBatchReadResult>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                PlcBatchReadRequest original = requests[i];
                PlcBatchReadResult result = results[i];
                remapped.Add(result.Success
                    ? PlcBatchReadResult.FromSuccess(original, result.Result!)
                    : PlcBatchReadResult.FromFailure(original, result.ErrorMessage, result.FailureScope));
            }
            return remapped;
        }
    }
}
