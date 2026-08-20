/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OmronFins
* 项目描述 ：
* 类 名 称 ：FinsClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OmronFins
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    
    
    
    
    
    
    
    
    
    public sealed class FinsClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const int MaximumTcpPayloadLength = 1024 * 1024;

        private readonly PlcConnectionOptions _options;
        private readonly FinsDriverOptions _driverOptions;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private UdpClient _udpClient;
        private IPEndPoint _udpRemoteEndPoint;
        private byte _clientNode;
        private byte _serverNode;
        private byte _sid;

        public FinsClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            _options = options;
            _driverOptions = FinsDriverOptions.Parse(options);
        }

        public bool IsConnected
        {
            get
            {
                if (_options.Transport == NetworkTransport.Udp)
                    return _udpClient != null;
                return _tcpClient != null && _tcpClient.Connected && _stream != null;
            }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.OmronFins; }
        }

        public void Connect()
        {
            Disconnect();

            int port = _options.Port <= 0 ? 9600 : _options.Port;
            if (_options.Transport == NetworkTransport.Udp)
            {
                IPAddress address = ResolveAddress(_options.Host);
                _udpRemoteEndPoint = new IPEndPoint(address, port);
                _udpClient = new UdpClient(address.AddressFamily);
                _udpClient.Client.ReceiveTimeout = GetTimeoutMilliseconds();
                _udpClient.Client.SendTimeout = GetTimeoutMilliseconds();
                _udpClient.Connect(_udpRemoteEndPoint);
                InitializeUdpNodes();
                return;
            }

            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = GetTimeoutMilliseconds();
            _tcpClient.SendTimeout = GetTimeoutMilliseconds();
            try
            {
                _tcpClient.ConnectAsync(_options.Host, port)
                    .WaitAsync(TimeSpan.FromMilliseconds(GetTimeoutMilliseconds()))
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                Disconnect();
                throw;
            }
            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = GetTimeoutMilliseconds();
            _stream.WriteTimeout = GetTimeoutMilliseconds();

            Handshake();
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();

            int port = _options.Port <= 0 ? 9600 : _options.Port;
            if (_options.Transport == NetworkTransport.Udp)
            {
                IPAddress address = await ResolveAddressAsync(_options.Host, cancellationToken).ConfigureAwait(false);
                _udpRemoteEndPoint = new IPEndPoint(address, port);
                _udpClient = new UdpClient(address.AddressFamily);
                _udpClient.Client.ReceiveTimeout = GetTimeoutMilliseconds();
                _udpClient.Client.SendTimeout = GetTimeoutMilliseconds();
                _udpClient.Connect(_udpRemoteEndPoint);
                InitializeUdpNodes();
                return;
            }

            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = GetTimeoutMilliseconds();
            _tcpClient.SendTimeout = GetTimeoutMilliseconds();
            using (CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(GetTimeoutMilliseconds());
                try
                {
                    await _tcpClient.ConnectAsync(_options.Host, port, connectTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Disconnect();
                    throw new TimeoutException("FINS/TCP 连接超时。");
                }
                catch
                {
                    Disconnect();
                    throw;
                }
            }
            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = GetTimeoutMilliseconds();
            _stream.WriteTimeout = GetTimeoutMilliseconds();

            await HandshakeAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }

            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }

            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            _udpRemoteEndPoint = null;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string addressText, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            EnsureSupportedType(dataType);
            if (elementCount <= 0)
                elementCount = 1;

            FinsAddress address = FinsAddress.Parse(addressText, dataType, _driverOptions);
            if (FinsDataCodec.IsBitType(dataType))
            {
                int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                start.EnsureRange(count, true);
                byte[] bitBytes = ReadMemory(start.Area.BitCode, start.WordAddress, start.BitIndex, count, _driverOptions.MaxBitCount);
                object value = FinsDataCodec.DecodeBits(dataType, bitBytes, count);
                return new PlcReadResult(start.Area.BitCode, start.Area.Name + ".BIT", value);
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("非 BOOL 类型不能使用 FINS 位地址。");

            bool usesCount = PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String;
            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            int words = FinsDataCodec.GetWordCount(dataType, usesCount ? elementCount : 1);
            wordStart.EnsureRange(words, false);
            byte[] data = ReadMemory(wordStart.Area.WordCode, wordStart.WordAddress, 0, words, _driverOptions.MaxWordCount);
            object result = FinsDataCodec.DecodeWords(dataType, data, usesCount ? elementCount : 1, _options.WordOrder);
            return new PlcReadResult(wordStart.Area.WordCode, wordStart.Area.Name + ".WORD", result);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string addressText,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            EnsureSupportedType(dataType);
            if (elementCount <= 0)
                elementCount = 1;

            FinsAddress address = FinsAddress.Parse(addressText, dataType, _driverOptions);
            if (FinsDataCodec.IsBitType(dataType))
            {
                int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                start.EnsureRange(count, true);
                byte[] bitBytes = await ReadMemoryAsync(start.Area.BitCode, start.WordAddress, start.BitIndex, count, _driverOptions.MaxBitCount, cancellationToken).ConfigureAwait(false);
                object value = FinsDataCodec.DecodeBits(dataType, bitBytes, count);
                return new PlcReadResult(start.Area.BitCode, start.Area.Name + ".BIT", value);
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("Non-BOOL FINS reads cannot use bit addresses.");

            bool usesCount = PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String;
            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            int words = FinsDataCodec.GetWordCount(dataType, usesCount ? elementCount : 1);
            wordStart.EnsureRange(words, false);
            byte[] data = await ReadMemoryAsync(wordStart.Area.WordCode, wordStart.WordAddress, 0, words, _driverOptions.MaxWordCount, cancellationToken).ConfigureAwait(false);
            object result = FinsDataCodec.DecodeWords(dataType, data, usesCount ? elementCount : 1, _options.WordOrder);
            return new PlcReadResult(wordStart.Area.WordCode, wordStart.Area.Name + ".WORD", result);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            EnsureConnected();
            return FinsBatchReadExecutor.ReadMany(requests, new FinsBatchReadContext
            {
                ReadMemory = ReadMemory,
                ReadMultipleMemory = ReadMultipleMemory,
                WordOrder = _options.WordOrder,
                MaxWordCount = _driverOptions.MaxWordCount,
                MaxBitCount = _driverOptions.MaxBitCount,
                MaxGapWords = _driverOptions.MaxGapWords,
                MaxSparseItems = _driverOptions.MaxSparseItems,
                DriverOptions = _driverOptions
            });
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            return await FinsBatchReadExecutor.ReadManyAsync(requests, new FinsAsyncBatchReadContext
            {
                ReadMemoryAsync = ReadMemoryAsync,
                ReadMultipleMemoryAsync = ReadMultipleMemoryAsync,
                WordOrder = _options.WordOrder,
                MaxWordCount = _driverOptions.MaxWordCount,
                MaxBitCount = _driverOptions.MaxBitCount,
                MaxGapWords = _driverOptions.MaxGapWords,
                MaxSparseItems = _driverOptions.MaxSparseItems,
                DriverOptions = _driverOptions
            }, cancellationToken).ConfigureAwait(false);
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            EnsureSupportedType(dataType);

            FinsAddress address = FinsAddress.Parse(addressText, dataType, _driverOptions);
            if (FinsDataCodec.IsBitType(dataType))
            {
                byte[] values = FinsDataCodec.EncodeBits(dataType, valueText);
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                start.EnsureRange(values.Length, true);
                WriteMemory(start.Area.BitCode, start.WordAddress, start.BitIndex, values, values.Length, _driverOptions.MaxBitCount);
                return;
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("非 BOOL 类型不能使用 FINS 位地址。");

            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            byte[] data = FinsDataCodec.EncodeWords(dataType, valueText, _options.WordOrder);
            wordStart.EnsureRange(data.Length / 2, false);
            WriteMemory(wordStart.Area.WordCode, wordStart.WordAddress, 0, data, data.Length / 2, _driverOptions.MaxWordCount);
        }

        public async ValueTask WriteAsync(
            string addressText,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            EnsureSupportedType(dataType);

            FinsAddress address = FinsAddress.Parse(addressText, dataType, _driverOptions);
            if (FinsDataCodec.IsBitType(dataType))
            {
                byte[] values = FinsDataCodec.EncodeBits(dataType, valueText);
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                start.EnsureRange(values.Length, true);
                await WriteMemoryAsync(start.Area.BitCode, start.WordAddress, start.BitIndex, values, values.Length, _driverOptions.MaxBitCount, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("Non-BOOL FINS writes cannot use bit addresses.");

            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            byte[] data = FinsDataCodec.EncodeWords(dataType, valueText, _options.WordOrder);
            wordStart.EnsureRange(data.Length / 2, false);
            await WriteMemoryAsync(wordStart.Area.WordCode, wordStart.WordAddress, 0, data, data.Length / 2, _driverOptions.MaxWordCount, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private byte[] ReadMemory(byte areaCode, int wordAddress, int bitIndex, int count, int segmentLimit)
        {
            MemoryStream result = new MemoryStream();
            int copied = 0;
            while (copied < count)
            {
                int segmentCount = Math.Min(segmentLimit, count - copied);
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, copied, areaCode);
                byte[] command = BuildMemoryCommand(0x01, 0x01, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, null);
                byte[] response = SendFinsCommand(command, true);
                ValidateMemoryResponse(response, 0x01, 0x01);
                int dataOffset = 14;
                int expectedBytes = IsBitAreaCode(areaCode) ? segmentCount : segmentCount * 2;
                if (response.Length < dataOffset + expectedBytes)
                    throw new InvalidOperationException("FINS 读取响应数据长度不足。");
                result.Write(response, dataOffset, expectedBytes);
                copied += segmentCount;
            }
            return result.ToArray();
        }

        private async ValueTask<byte[]> ReadMemoryAsync(
            byte areaCode,
            int wordAddress,
            int bitIndex,
            int count,
            int segmentLimit,
            CancellationToken cancellationToken)
        {
            MemoryStream result = new MemoryStream();
            int copied = 0;
            while (copied < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentCount = Math.Min(segmentLimit, count - copied);
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, copied, areaCode);
                byte[] command = BuildMemoryCommand(0x01, 0x01, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, null);
                byte[] response = await SendFinsCommandAsync(command, true, cancellationToken).ConfigureAwait(false);
                ValidateMemoryResponse(response, 0x01, 0x01);
                int dataOffset = 14;
                int expectedBytes = IsBitAreaCode(areaCode) ? segmentCount : segmentCount * 2;
                if (response.Length < dataOffset + expectedBytes)
                    throw new InvalidOperationException("FINS read response data is too short.");
                result.Write(response, dataOffset, expectedBytes);
                copied += segmentCount;
            }
            return result.ToArray();
        }

        private byte[] ReadMultipleMemory(IList<FinsMemoryPoint> points)
        {
            byte[] command = BuildMultipleMemoryReadCommand(points);
            byte[] response = SendFinsCommand(command, true);
            ValidateMemoryResponse(response, 0x01, 0x04);
            return DecodeMultipleMemoryResponse(response, points);
        }

        private async ValueTask<byte[]> ReadMultipleMemoryAsync(
            IList<FinsMemoryPoint> points,
            CancellationToken cancellationToken)
        {
            byte[] command = BuildMultipleMemoryReadCommand(points);
            byte[] response = await SendFinsCommandAsync(command, true, cancellationToken).ConfigureAwait(false);
            ValidateMemoryResponse(response, 0x01, 0x04);
            return DecodeMultipleMemoryResponse(response, points);
        }

        private static byte[] BuildMultipleMemoryReadCommand(IList<FinsMemoryPoint> points)
        {
            if (points == null || points.Count == 0 || points.Count > 167)
                throw new ArgumentOutOfRangeException("points");

            byte[] command = new byte[2 + points.Count * 4];
            command[0] = 0x01;
            command[1] = 0x04;
            for (int i = 0; i < points.Count; i++)
            {
                FinsMemoryPoint point = points[i];
                int offset = 2 + i * 4;
                command[offset] = point.AreaCode;
                command[offset + 1] = (byte)(point.WordAddress >> 8);
                command[offset + 2] = (byte)(point.WordAddress & 0xFF);
                command[offset + 3] = (byte)point.BitIndex;
            }
            return command;
        }

        private static byte[] DecodeMultipleMemoryResponse(byte[] response, IList<FinsMemoryPoint> points)
        {
            using MemoryStream data = new MemoryStream();
            int offset = 14;
            for (int i = 0; i < points.Count; i++)
            {
                FinsMemoryPoint point = points[i];
                int required = 1 + point.ByteCount;
                if (response.Length < offset + required)
                    throw new IOException("FINS multiple-memory response is too short.");
                if (response[offset] != point.AreaCode)
                    throw new IOException("FINS multiple-memory response area code mismatch.");
                data.Write(response, offset + 1, point.ByteCount);
                offset += required;
            }
            return data.ToArray();
        }

        private void WriteMemory(byte areaCode, int wordAddress, int bitIndex, byte[] data, int count, int segmentLimit)
        {
            int written = 0;
            int bytesPerElement = IsBitAreaCode(areaCode) ? 1 : 2;
            while (written < count)
            {
                int segmentCount = Math.Min(segmentLimit, count - written);
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, written, areaCode);
                byte[] segmentData = new byte[segmentCount * bytesPerElement];
                Buffer.BlockCopy(data, written * bytesPerElement, segmentData, 0, segmentData.Length);
                byte[] command = BuildMemoryCommand(0x01, 0x02, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, segmentData);
                byte[] response = SendFinsCommand(command, false);
                ValidateMemoryResponse(response, 0x01, 0x02);
                written += segmentCount;
            }
        }

        private async ValueTask WriteMemoryAsync(
            byte areaCode,
            int wordAddress,
            int bitIndex,
            byte[] data,
            int count,
            int segmentLimit,
            CancellationToken cancellationToken)
        {
            int written = 0;
            int bytesPerElement = IsBitAreaCode(areaCode) ? 1 : 2;
            while (written < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentCount = Math.Min(segmentLimit, count - written);
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, written, areaCode);
                byte[] segmentData = new byte[segmentCount * bytesPerElement];
                Buffer.BlockCopy(data, written * bytesPerElement, segmentData, 0, segmentData.Length);
                byte[] command = BuildMemoryCommand(0x01, 0x02, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, segmentData);
                byte[] response = await SendFinsCommandAsync(command, false, cancellationToken).ConfigureAwait(false);
                ValidateMemoryResponse(response, 0x01, 0x02);
                written += segmentCount;
            }
        }

        private void Handshake()
        {
            byte[] request = new byte[20];
            byte[] magic = Encoding.ASCII.GetBytes("FINS");
            Buffer.BlockCopy(magic, 0, request, 0, magic.Length);
            WriteUInt32(request, 4, 12);
            WriteUInt32(request, 8, 0);
            WriteUInt32(request, 12, 0);
            WriteUInt32(request, 16, _driverOptions.SourceNode);
            _stream.Write(request, 0, request.Length);

            byte[] header = ReadExact(16);
            ValidateFinsTcpHeader(header);
            uint length = ReadUInt32(header, 4);
            uint command = ReadUInt32(header, 8);
            uint error = ReadUInt32(header, 12);
            if (command != 1)
                throw new InvalidOperationException("FINS/TCP 握手响应命令不正确。");
            if (error != 0)
                throw new InvalidOperationException("FINS/TCP 握手失败: 0x" + error.ToString("X8"));
            if (length < 16)
                throw new InvalidOperationException("FINS/TCP 握手响应长度不足。");

            byte[] payload = ReadExact(ValidateTcpPayloadLength(length));
            _clientNode = (byte)ReadUInt32(payload, 0);
            _serverNode = (byte)ReadUInt32(payload, 4);
            ApplyConfiguredNodes();
        }

        private async ValueTask HandshakeAsync(CancellationToken cancellationToken)
        {
            byte[] request = new byte[20];
            byte[] magic = Encoding.ASCII.GetBytes("FINS");
            Buffer.BlockCopy(magic, 0, request, 0, magic.Length);
            WriteUInt32(request, 4, 12);
            WriteUInt32(request, 8, 0);
            WriteUInt32(request, 12, 0);
            WriteUInt32(request, 16, _driverOptions.SourceNode);
            await _stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);

            byte[] header = await ReadExactAsync(16, cancellationToken).ConfigureAwait(false);
            ValidateFinsTcpHeader(header);
            uint length = ReadUInt32(header, 4);
            uint command = ReadUInt32(header, 8);
            uint error = ReadUInt32(header, 12);
            if (command != 1)
                throw new InvalidOperationException("FINS/TCP handshake response command is invalid.");
            if (error != 0)
                throw new InvalidOperationException("FINS/TCP handshake failed: 0x" + error.ToString("X8"));
            if (length < 16)
                throw new InvalidOperationException("FINS/TCP handshake response is too short.");

            byte[] payload = await ReadExactAsync(ValidateTcpPayloadLength(length), cancellationToken).ConfigureAwait(false);
            _clientNode = (byte)ReadUInt32(payload, 0);
            _serverNode = (byte)ReadUInt32(payload, 4);
            ApplyConfiguredNodes();
        }

        private byte[] SendFinsCommand(byte[] command, bool allowUdpReadRetry)
        {
            byte[] finsFrame = BuildFinsFrame(command, out byte sid);
            if (_options.Transport == NetworkTransport.Udp)
                return SendUdp(finsFrame, sid, allowUdpReadRetry);

            byte[] packet = new byte[16 + finsFrame.Length];
            byte[] magic = Encoding.ASCII.GetBytes("FINS");
            Buffer.BlockCopy(magic, 0, packet, 0, magic.Length);
            WriteUInt32(packet, 4, 8 + finsFrame.Length);
            WriteUInt32(packet, 8, 2);
            WriteUInt32(packet, 12, 0);
            Buffer.BlockCopy(finsFrame, 0, packet, 16, finsFrame.Length);
            _stream.Write(packet, 0, packet.Length);

            byte[] header = ReadExact(16);
            ValidateFinsTcpHeader(header);
            uint length = ReadUInt32(header, 4);
            uint tcpCommand = ReadUInt32(header, 8);
            uint error = ReadUInt32(header, 12);
            if (tcpCommand != 2)
                throw new InvalidOperationException("FINS/TCP 响应命令不正确。");
            if (error != 0)
                throw new InvalidOperationException("FINS/TCP 响应错误: 0x" + error.ToString("X8"));
            int payloadLength = ValidateTcpPayloadLength(length);
            return ValidateFinsResponseFrame(ReadExact(payloadLength), sid);
        }

        private async ValueTask<byte[]> SendFinsCommandAsync(
            byte[] command,
            bool allowUdpReadRetry,
            CancellationToken cancellationToken)
        {
            byte[] finsFrame = BuildFinsFrame(command, out byte sid);
            if (_options.Transport == NetworkTransport.Udp)
                return await SendUdpAsync(finsFrame, sid, allowUdpReadRetry, cancellationToken).ConfigureAwait(false);

            byte[] packet = new byte[16 + finsFrame.Length];
            byte[] magic = Encoding.ASCII.GetBytes("FINS");
            Buffer.BlockCopy(magic, 0, packet, 0, magic.Length);
            WriteUInt32(packet, 4, 8 + finsFrame.Length);
            WriteUInt32(packet, 8, 2);
            WriteUInt32(packet, 12, 0);
            Buffer.BlockCopy(finsFrame, 0, packet, 16, finsFrame.Length);
            await _stream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);

            byte[] header = await ReadExactAsync(16, cancellationToken).ConfigureAwait(false);
            ValidateFinsTcpHeader(header);
            uint length = ReadUInt32(header, 4);
            uint tcpCommand = ReadUInt32(header, 8);
            uint error = ReadUInt32(header, 12);
            if (tcpCommand != 2)
                throw new InvalidOperationException("FINS/TCP response command is invalid.");
            if (error != 0)
                throw new InvalidOperationException("FINS/TCP response error: 0x" + error.ToString("X8"));
            int payloadLength = ValidateTcpPayloadLength(length);
            byte[] response = await ReadExactAsync(payloadLength, cancellationToken).ConfigureAwait(false);
            return ValidateFinsResponseFrame(response, sid);
        }

        private byte[] SendUdp(byte[] request, byte sid, bool allowReadRetry)
        {
            int retryCount = allowReadRetry ? _driverOptions.UdpReadRetries : 0;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    DrainPendingUdpResponses();
                    _udpClient.Send(request, request.Length);
                    IPEndPoint remote = null;
                    byte[] response = _udpClient.Receive(ref remote);
                    ValidateUdpRemoteEndPoint(remote);
                    return ValidateFinsResponseFrame(response, sid);
                }
                catch (Exception ex) when (IsRetryableUdpFailure(ex))
                {
                    if (attempt >= retryCount)
                        throw new TimeoutException("Omron FINS/UDP request timed out.", ex);
                    Thread.Sleep(Random.Shared.Next(25, 101));
                }
            }
        }

        private async ValueTask<byte[]> SendUdpAsync(
            byte[] request,
            byte sid,
            bool allowReadRetry,
            CancellationToken cancellationToken)
        {
            int retryCount = allowReadRetry ? _driverOptions.UdpReadRetries : 0;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    DrainPendingUdpResponses();
                    using CancellationTokenSource timeout = CreateUdpTimeout(cancellationToken);
                    await _udpClient.SendAsync(request, request.Length).WaitAsync(timeout.Token).ConfigureAwait(false);
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    ValidateUdpRemoteEndPoint(result.RemoteEndPoint);
                    return ValidateFinsResponseFrame(result.Buffer, sid);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= retryCount)
                        throw new TimeoutException("Omron FINS/UDP request timed out.", ex);
                    await Task.Delay(Random.Shared.Next(25, 101), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryableUdpFailure(ex))
                {
                    if (attempt >= retryCount)
                        throw new TimeoutException("Omron FINS/UDP request timed out.", ex);
                    await Task.Delay(Random.Shared.Next(25, 101), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static bool IsRetryableUdpFailure(Exception exception)
        {
            if (exception is TimeoutException || exception is IOException)
                return true;
            if (exception is not SocketException socketException)
                return false;
            return socketException.SocketErrorCode == SocketError.TimedOut ||
                   socketException.SocketErrorCode == SocketError.WouldBlock ||
                   socketException.SocketErrorCode == SocketError.ConnectionReset ||
                   socketException.SocketErrorCode == SocketError.NetworkReset ||
                   socketException.SocketErrorCode == SocketError.HostUnreachable;
        }

        private CancellationTokenSource CreateUdpTimeout(CancellationToken cancellationToken)
        {
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetTimeoutMilliseconds());
            return timeout;
        }

        private void DrainPendingUdpResponses()
        {
            while (_udpClient != null && _udpClient.Available > 0)
            {
                IPEndPoint remote = null;
                _udpClient.Receive(ref remote);
            }
        }

        private void ValidateUdpRemoteEndPoint(IPEndPoint remote)
        {
            if (_udpRemoteEndPoint == null || remote == null ||
                !_udpRemoteEndPoint.Address.Equals(remote.Address) ||
                _udpRemoteEndPoint.Port != remote.Port)
                throw new IOException("FINS/UDP response endpoint mismatch.");
        }

        private byte[] BuildFinsFrame(byte[] command, out byte sid)
        {
            byte[] frame = new byte[10 + command.Length];
            frame[0] = 0x80;
            frame[1] = 0x00;
            frame[2] = 0x02;
            frame[3] = _driverOptions.DestinationNetwork;
            frame[4] = _serverNode;
            frame[5] = _driverOptions.DestinationUnit;
            frame[6] = _driverOptions.SourceNetwork;
            frame[7] = _clientNode;
            frame[8] = _driverOptions.SourceUnit;
            sid = NextSid();
            frame[9] = sid;
            Buffer.BlockCopy(command, 0, frame, 10, command.Length);
            return frame;
        }

        private static byte[] BuildMemoryCommand(byte mainCommand, byte subCommand, byte areaCode, int wordAddress, int bitIndex, int count, byte[] data)
        {
            int dataLength = data == null ? 0 : data.Length;
            byte[] command = new byte[8 + dataLength];
            command[0] = mainCommand;
            command[1] = subCommand;
            command[2] = areaCode;
            command[3] = (byte)(wordAddress >> 8);
            command[4] = (byte)(wordAddress & 0xFF);
            command[5] = (byte)bitIndex;
            command[6] = (byte)(count >> 8);
            command[7] = (byte)(count & 0xFF);
            if (dataLength > 0)
                Buffer.BlockCopy(data, 0, command, 8, dataLength);
            return command;
        }

        private static void ValidateMemoryResponse(byte[] response, byte mainCommand, byte subCommand)
        {
            if (response == null || response.Length < 14)
                throw new InvalidOperationException("FINS 响应长度不足。");
            if (response[10] != mainCommand || response[11] != subCommand)
                throw new InvalidOperationException("FINS 响应命令不匹配。");
            ushort endCode = ReadUInt16(response, 12);
            if (endCode != 0)
                throw new FinsProtocolException(
                    endCode,
                    "FINS错误: end code 0x" + endCode.ToString("X4") + " (" + GetEndCodeName(endCode) + ")",
                    GetEndCodeScope(endCode));
        }

        private byte[] ValidateFinsResponseFrame(byte[] response, byte expectedSid)
        {
            if (response == null || response.Length < 10)
                throw new IOException("FINS response frame is too short.");
            if ((response[0] & 0x40) == 0)
                throw new IOException("FINS response flag is missing.");
            if (response[9] != expectedSid)
                throw new IOException("FINS response SID mismatch.");
            if (response[4] != _clientNode || response[7] != _serverNode)
                throw new IOException("FINS response node address mismatch.");
            return response;
        }

        private static int ValidateTcpPayloadLength(uint length)
        {
            if (length < 8 || length - 8 > MaximumTcpPayloadLength)
                throw new IOException("FINS/TCP response length is invalid: " + length);
            return checked((int)length - 8);
        }

        private static FinsErrorScope GetEndCodeScope(ushort endCode)
        {
            if (endCode == 0x0401)
                return FinsErrorScope.Tag;
            int mainCode = endCode >> 8;
            if (mainCode >= 0x01 && mainCode <= 0x05)
                return FinsErrorScope.Device;
            if (mainCode == 0x20 || mainCode == 0x22 || mainCode == 0x23 || mainCode == 0x25)
                return FinsErrorScope.Device;
            return FinsErrorScope.Tag;
        }

        private static AddressParts OffsetAddress(int wordAddress, int bitIndex, int offset, byte areaCode)
        {
            if (!IsBitAreaCode(areaCode) || IsWordIndexedBitAreaCode(areaCode))
                return new AddressParts(wordAddress + offset, bitIndex);

            int absoluteBit = bitIndex + offset;
            return new AddressParts(wordAddress + absoluteBit / 16, absoluteBit % 16);
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                Connect();
        }

        private async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void EnsureSupportedType(PlcDataType dataType)
        {
            if (dataType == PlcDataType.Coil ||
                dataType == PlcDataType.CoilArray ||
                dataType == PlcDataType.DiscreteInput ||
                dataType == PlcDataType.DiscreteInputArray)
                throw new NotSupportedException("FINS 协议不使用 Modbus Coil/Discrete Input 类型，请选择 BOOL 或 BOOL[]。");
        }

        private void ApplyConfiguredNodes()
        {
            if (_driverOptions.SourceNode != 0)
                _clientNode = _driverOptions.SourceNode;
            if (_driverOptions.DestinationNode != 0)
                _serverNode = _driverOptions.DestinationNode;
            if (_clientNode == 0)
                _clientNode = 0xEF;
            if (_serverNode == 0)
                throw new InvalidOperationException("FINS 目标节点号不能为 0。");
        }

        private void InitializeUdpNodes()
        {
            IPEndPoint local = _udpClient.Client.LocalEndPoint as IPEndPoint;
            _clientNode = _driverOptions.SourceNode != 0
                ? _driverOptions.SourceNode
                : GetNodeFromAddress(local?.Address, "源");
            _serverNode = _driverOptions.DestinationNode != 0
                ? _driverOptions.DestinationNode
                : GetNodeFromAddress(_udpRemoteEndPoint?.Address, "目标");
        }

        private static byte GetNodeFromAddress(IPAddress address, string role)
        {
            byte[] bytes = address?.GetAddressBytes();
            if (bytes == null || bytes.Length != 4 || bytes[3] == 0 || bytes[3] == 255)
                throw new InvalidOperationException("无法从 IP 地址推导 FINS " + role + "节点号，请显式配置节点号。");
            return bytes[3];
        }

        private static IPAddress ResolveAddress(string host)
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            return SelectAddress(addresses, host);
        }

        private static async ValueTask<IPAddress> ResolveAddressAsync(string host, CancellationToken cancellationToken)
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return SelectAddress(addresses, host);
        }

        private static IPAddress SelectAddress(IPAddress[] addresses, string host)
        {
            if (addresses != null)
            {
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                        return addresses[i];
                }
                if (addresses.Length > 0)
                    return addresses[0];
            }
            throw new InvalidOperationException("PLC 地址解析失败: " + host);
        }

        private int GetTimeoutMilliseconds()
        {
            return _options.TimeoutMilliseconds > 0 ? _options.TimeoutMilliseconds : 3000;
        }

        private byte NextSid()
        {
            _sid++;
            if (_sid == 0)
                _sid = 1;
            return _sid;
        }

        private static bool IsBitAreaCode(byte areaCode)
        {
            return areaCode == 0x30 ||
                   areaCode == 0x31 ||
                   areaCode == 0x32 ||
                   areaCode == 0x33 ||
                   areaCode == 0x02 ||
                   areaCode == 0x09 ||
                   areaCode == 0x0A ||
                   (areaCode >= 0x20 && areaCode <= 0x2F) ||
                   (areaCode >= 0xE0 && areaCode <= 0xE8);
        }

        private static bool IsWordIndexedBitAreaCode(byte areaCode)
        {
            return areaCode == 0x09;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("FINS TCP 连接已断开。");
                offset += read;
            }
            return buffer;
        }

        private async ValueTask<byte[]> ReadExactAsync(
            int count,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await _stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new IOException("FINS TCP connection was closed.");
                offset += read;
            }
            return buffer;
        }

        private static void ValidateFinsTcpHeader(byte[] header)
        {
            if (header == null || header.Length < 16 ||
                header[0] != (byte)'F' ||
                header[1] != (byte)'I' ||
                header[2] != (byte)'N' ||
                header[3] != (byte)'S')
                throw new InvalidOperationException("FINS/TCP 响应头不正确。");
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static void WriteUInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)(value & 0xFF);
        }

        private static string GetEndCodeName(ushort code)
        {
            switch (code)
            {
                case 0x0000:
                    return "Normal completion";
                case 0x0101:
                    return "Local node not in network";
                case 0x0102:
                    return "Token timeout";
                case 0x0201:
                    return "Destination node not in network";
                case 0x0202:
                    return "Unit missing";
                case 0x0401:
                    return "Undefined command";
                case 0x1001:
                    return "Command too large";
                case 0x1002:
                    return "Command too small";
                case 0x1101:
                    return "Area type missing";
                case 0x1103:
                    return "Address range error";
                case 0x2101:
                    return "Read-only area";
                case 0x2201:
                    return "Not executable in current mode";
                default:
                    return "Unknown";
            }
        }

        private struct AddressParts
        {
            public AddressParts(int wordAddress, int bitIndex)
            {
                WordAddress = wordAddress;
                BitIndex = bitIndex;
            }

            public int WordAddress;
            public int BitIndex;
        }
    }
}
