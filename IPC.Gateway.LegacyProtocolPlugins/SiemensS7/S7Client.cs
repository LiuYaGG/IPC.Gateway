/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.SiemensS7
* 项目描述 ：
* 类 名 称 ：S7Client
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.SiemensS7
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    
    
    
    
    
    
    
    
    
    public sealed class S7Client : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const int DefaultPduSize = 240;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private ushort _sequence;
        private readonly PlcConnectionOptions _options;
        private readonly int _maxItemsPerRequest;
        private int _pduSize;

        public S7Client(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            _options = options;
            if (_options.Port <= 0)
                _options.Port = 102;
            S7DriverOptions driverOptions = S7DriverOptions.Parse(options);
            ControllerProfile = driverOptions.ControllerProfile;
            LocalTsap = driverOptions.LocalTsap;
            RemoteTsap = driverOptions.ResolveRemoteTsap(options.Rack, options.Slot);
            _maxItemsPerRequest = driverOptions.MaxItemsPerRequest;
            _pduSize = DefaultPduSize;
        }

        public string ControllerProfile { get; private set; }
        public ushort LocalTsap { get; private set; }
        public ushort RemoteTsap { get; private set; }
        public bool IsConnected { get { return _tcpClient != null && _tcpClient.Connected && _stream != null; } }
        public PlcProtocol Protocol { get { return PlcProtocol.SiemensS7; } }

        public void Connect()
        {
            if (IsConnected)
                return;

            _tcpClient = new TcpClient();
            IAsyncResult async = _tcpClient.BeginConnect(_options.Host, _options.Port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(_options.TimeoutMilliseconds))
                throw new TimeoutException("连接 Siemens S7 PLC 超时。");

            _tcpClient.EndConnect(async);
            _tcpClient.ReceiveTimeout = _options.TimeoutMilliseconds;
            _tcpClient.SendTimeout = _options.TimeoutMilliseconds;
            _stream = _tcpClient.GetStream();

            SendIsoConnectionRequest();
            SendSetupCommunication();
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
                return;

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);
            _tcpClient.ReceiveTimeout = _options.TimeoutMilliseconds;
            _tcpClient.SendTimeout = _options.TimeoutMilliseconds;
            _stream = _tcpClient.GetStream();

            await SendIsoConnectionRequestAsync(cancellationToken).ConfigureAwait(false);
            await SendSetupCommunicationAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            if (_stream != null)
                _stream.Dispose();
            if (_tcpClient != null)
                _tcpClient.Close();
            _stream = null;
            _tcpClient = null;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            S7Address s7Address = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.String)
            {
                int stringByteCount = S7DataCodec.GetReadByteCount(dataType, s7Address.BitOffset, elementCount);
                byte[] stringData = ReadBytes(s7Address, stringByteCount);
                object stringValue = S7DataCodec.Decode(dataType, stringData, 0, elementCount);
                return new PlcReadResult(0, GetTypeName(dataType), stringValue);
            }

            int byteCount = S7DataCodec.GetReadByteCount(dataType, s7Address.BitOffset, elementCount);
            byte[] data = ReadBytes(s7Address, byteCount);
            object value = S7DataCodec.Decode(dataType, data, s7Address.BitOffset, elementCount);
            return new PlcReadResult(0, GetTypeName(dataType), value);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            S7Address s7Address = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.String)
            {
                int stringByteCount = S7DataCodec.GetReadByteCount(dataType, s7Address.BitOffset, elementCount);
                byte[] stringData = await ReadBytesAsync(s7Address, stringByteCount, cancellationToken).ConfigureAwait(false);
                object stringValue = S7DataCodec.Decode(dataType, stringData, 0, elementCount);
                return new PlcReadResult(0, GetTypeName(dataType), stringValue);
            }

            int byteCount = S7DataCodec.GetReadByteCount(dataType, s7Address.BitOffset, elementCount);
            byte[] data = await ReadBytesAsync(s7Address, byteCount, cancellationToken).ConfigureAwait(false);
            object value = S7DataCodec.Decode(dataType, data, s7Address.BitOffset, elementCount);
            return new PlcReadResult(0, GetTypeName(dataType), value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            if (!IsConnected)
                Connect();

            if (CanUseContiguousReadPlan(requests))
            {
                return S7BatchReadExecutor.ReadMany(requests, new S7BatchReadContext
                {
                    BuildAddress = BuildAddress,
                    ReadBytes = ReadBytes,
                    GetTypeName = GetTypeName,
                    MaxReadBytes = GetMaxDataBytesPerPacket()
                });
            }
            return S7MultiReadExecutor.ReadMany(requests, this);
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (CanUseContiguousReadPlan(requests))
            {
                return await S7BatchReadExecutor.ReadManyAsync(requests, new S7AsyncBatchReadContext
                {
                    BuildAddress = BuildAddress,
                    ReadBytesAsync = ReadBytesAsync,
                    GetTypeName = GetTypeName,
                    MaxReadBytes = GetMaxDataBytesPerPacket()
                }, cancellationToken).ConfigureAwait(false);
            }
            return await S7MultiReadExecutor.ReadManyAsync(requests, this, cancellationToken).ConfigureAwait(false);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            int elementCount = S7DataCodec.GetElementCount(dataType, valueText, 1);
            S7Address s7Address = BuildAddress(address, dataType, elementOffset);
            byte[] data = S7DataCodec.Encode(dataType, valueText);

            if (S7DataCodec.IsBoolType(dataType))
            {
                WriteBits(s7Address, data, elementCount);
                return;
            }

            if (dataType == PlcDataType.String)
            {
                int maxLength = ReadStringMaxLength(s7Address);
                data = S7DataCodec.EncodeString(valueText, maxLength);
            }

            WriteBytes(s7Address, data);
        }

        public async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            int elementCount = S7DataCodec.GetElementCount(dataType, valueText, 1);
            S7Address s7Address = BuildAddress(address, dataType, elementOffset);
            byte[] data = S7DataCodec.Encode(dataType, valueText);

            if (S7DataCodec.IsBoolType(dataType))
            {
                await WriteBitsAsync(s7Address, data, elementCount, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (dataType == PlcDataType.String)
            {
                int maxLength = await ReadStringMaxLengthAsync(s7Address, cancellationToken).ConfigureAwait(false);
                data = S7DataCodec.EncodeString(valueText, maxLength);
            }

            await WriteBytesAsync(s7Address, data, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Disconnect();
        }

        internal int MaxItemsPerRequest { get { return _maxItemsPerRequest; } }
        internal int NegotiatedPduSize { get { return Math.Max(64, _pduSize); } }

        internal S7Address BuildBatchAddress(PlcBatchReadRequest request)
        {
            return BuildAddress(request.Address, request.DataType, request.ElementOffset);
        }

        internal string GetBatchTypeName(PlcDataType dataType)
        {
            return GetTypeName(dataType);
        }

        private bool CanUseContiguousReadPlan(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null || requests.Count < 2)
                return false;

            try
            {
                S7BatchReadContext context = new S7BatchReadContext { BuildAddress = BuildAddress };
                List<S7BatchReadItem> items = new List<S7BatchReadItem>(requests.Count);
                for (int i = 0; i < requests.Count; i++)
                {
                    if (requests[i] == null)
                        return false;
                    items.Add(S7BatchReadItem.Create(i, requests[i], context));
                }

                string groupKey = items[0].GroupKey;
                if (items.Exists(item => !string.Equals(item.GroupKey, groupKey, StringComparison.Ordinal)))
                    return false;
                items.Sort((left, right) => left.StartByte.CompareTo(right.StartByte));
                for (int i = 1; i < items.Count; i++)
                {
                    if (items[i].StartByte > items[i - 1].EndByte + 1)
                        return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private S7Address BuildAddress(string address, PlcDataType dataType, int elementOffset)
        {
            S7Address parsed = S7Address.Parse(address, dataType);
            if (S7DataCodec.IsBoolType(dataType))
                return parsed.AddBitOffset(elementOffset);
            if (PlcDataTypeHelper.IsArray(dataType))
                return parsed.AddByteOffset(elementOffset * PlcDataTypeHelper.GetElementSize(dataType));
            return parsed;
        }

        private byte[] ReadBytes(S7Address address, int byteCount)
        {
            if (!IsConnected)
                Connect();

            if (byteCount <= 0)
                throw new ArgumentOutOfRangeException("byteCount");

            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < byteCount)
            {
                int chunk = Math.Min(GetMaxDataBytesPerPacket(), byteCount - offset);
                S7Address chunkAddress = address.AddByteOffset(offset);
                byte[] response = SendS7(BuildReadRequest(chunkAddress, chunk));
                byte[] data = ParseReadResponse(response);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }

            return result.ToArray();
        }

        private async ValueTask<byte[]> ReadBytesAsync(
            S7Address address,
            int byteCount,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (byteCount <= 0)
                throw new ArgumentOutOfRangeException("byteCount");

            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < byteCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(GetMaxDataBytesPerPacket(), byteCount - offset);
                S7Address chunkAddress = address.AddByteOffset(offset);
                byte[] response = await SendS7Async(BuildReadRequest(chunkAddress, chunk), cancellationToken).ConfigureAwait(false);
                byte[] data = ParseReadResponse(response);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }

            return result.ToArray();
        }

        private void WriteBytes(S7Address address, byte[] data)
        {
            if (!IsConnected)
                Connect();

            if (data == null || data.Length == 0)
                throw new ArgumentException("写入数据不能为空。", "data");

            int offset = 0;
            while (offset < data.Length)
            {
                int chunk = Math.Min(GetMaxWriteDataBytesPerPacket(), data.Length - offset);
                byte[] chunkData = Slice(data, offset, chunk);
                S7Address chunkAddress = address.AddByteOffset(offset);
                byte[] response = SendS7(BuildWriteRequest(chunkAddress, chunkData));
                ParseWriteResponse(response);
                offset += chunk;
            }
        }

        private async ValueTask WriteBytesAsync(
            S7Address address,
            byte[] data,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (data == null || data.Length == 0)
                throw new ArgumentException("Write data cannot be empty.", "data");

            int offset = 0;
            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(GetMaxWriteDataBytesPerPacket(), data.Length - offset);
                byte[] chunkData = Slice(data, offset, chunk);
                S7Address chunkAddress = address.AddByteOffset(offset);
                byte[] response = await SendS7Async(BuildWriteRequest(chunkAddress, chunkData), cancellationToken).ConfigureAwait(false);
                ParseWriteResponse(response);
                offset += chunk;
            }
        }

        private void WriteBits(S7Address address, byte[] values, int bitCount)
        {
            if (!IsConnected)
                Connect();
            byte[] response = SendS7(BuildBitWriteRequest(address, values, bitCount));
            ParseWriteResponse(response);
        }

        private async ValueTask WriteBitsAsync(
            S7Address address,
            byte[] values,
            int bitCount,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            byte[] request = BuildBitWriteRequest(address, values, bitCount);
            byte[] response = await SendS7Async(request, cancellationToken).ConfigureAwait(false);
            ParseWriteResponse(response);
        }

        private int ReadStringMaxLength(S7Address address)
        {
            try
            {
                byte[] header = ReadBytes(address, 2);
                int max = header[0];
                if (max > 0 && max <= 254)
                    return max;
            }
            catch
            {
            }

            return 254;
        }

        private async ValueTask<int> ReadStringMaxLengthAsync(
            S7Address address,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] header = await ReadBytesAsync(address, 2, cancellationToken).ConfigureAwait(false);
                int max = header[0];
                if (max > 0 && max <= 254)
                    return max;
            }
            catch
            {
            }

            return 254;
        }

        private int GetMaxDataBytesPerPacket()
        {
            return Math.Max(1, _pduSize - 18);
        }

        private int GetMaxWriteDataBytesPerPacket()
        {
            return Math.Max(1, _pduSize - 28);
        }

        private void SendIsoConnectionRequest()
        {
            byte[] packet = new byte[]
            {
                0x03, 0x00, 0x00, 0x16,
                0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
                0xC0, 0x01, 0x0A,
                0xC1, 0x02, (byte)(LocalTsap >> 8), (byte)LocalTsap,
                0xC2, 0x02, (byte)(RemoteTsap >> 8), (byte)RemoteTsap
            };

            _stream.Write(packet, 0, packet.Length);
            byte[] response = ReadTpkt();
            if (response.Length < 7 || response[5] != 0xD0)
                throw new InvalidOperationException("ISO-on-TCP 连接确认失败。");
        }

        private async ValueTask SendIsoConnectionRequestAsync(CancellationToken cancellationToken)
        {
            byte[] packet = new byte[]
            {
                0x03, 0x00, 0x00, 0x16,
                0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
                0xC0, 0x01, 0x0A,
                0xC1, 0x02, (byte)(LocalTsap >> 8), (byte)LocalTsap,
                0xC2, 0x02, (byte)(RemoteTsap >> 8), (byte)RemoteTsap
            };

            await _stream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
            byte[] response = await ReadTpktAsync(cancellationToken).ConfigureAwait(false);
            if (response.Length < 7 || response[5] != 0xD0)
                throw new InvalidOperationException("ISO-on-TCP connection confirm failed.");
        }

        private void SendSetupCommunication()
        {
            byte[] request = new byte[]
            {
                0x32, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00, 0x00,
                0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0
            };

            byte[] response = SendS7(request);
            int s7 = GetS7Offset(response);
            if (response.Length >= s7 + 20)
                _pduSize = (response[s7 + 18] << 8) | response[s7 + 19];
            if (_pduSize <= 0)
                _pduSize = DefaultPduSize;
        }

        private async ValueTask SendSetupCommunicationAsync(CancellationToken cancellationToken)
        {
            byte[] request = new byte[]
            {
                0x32, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00, 0x00,
                0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0
            };

            byte[] response = await SendS7Async(request, cancellationToken).ConfigureAwait(false);
            int s7 = GetS7Offset(response);
            if (response.Length >= s7 + 20)
                _pduSize = (response[s7 + 18] << 8) | response[s7 + 19];
            if (_pduSize <= 0)
                _pduSize = DefaultPduSize;
        }

        private byte[] BuildReadRequest(S7Address address, int byteCount)
        {
            MemoryStream stream = new MemoryStream();
            WriteS7JobHeader(stream, 14, 0);
            stream.WriteByte(0x04);
            stream.WriteByte(0x01);
            WriteAddressItem(stream, address, byteCount);
            return stream.ToArray();
        }

        internal void ReadItemBatch(IList<S7MultiReadItem> items)
        {
            byte[] response = SendS7(BuildMultiReadRequest(items));
            ParseMultiReadResponse(response, items);
        }

        internal async ValueTask ReadItemBatchAsync(
            IList<S7MultiReadItem> items,
            CancellationToken cancellationToken)
        {
            byte[] request = BuildMultiReadRequest(items);
            byte[] response = await SendS7Async(request, cancellationToken).ConfigureAwait(false);
            ParseMultiReadResponse(response, items);
        }

        internal byte[] BuildMultiReadRequest(IList<S7MultiReadItem> items)
        {
            if (items == null || items.Count == 0 || items.Count > 255)
                throw new ArgumentOutOfRangeException(nameof(items));

            MemoryStream stream = new MemoryStream();
            WriteS7JobHeader(stream, 2 + (12 * items.Count), 0);
            stream.WriteByte(0x04);
            stream.WriteByte((byte)items.Count);
            for (int i = 0; i < items.Count; i++)
                WriteAddressItem(stream, items[i].Address, items[i].ByteCount);
            return stream.ToArray();
        }

        private byte[] BuildWriteRequest(S7Address address, byte[] data)
        {
            int dataLength = 4 + data.Length + (data.Length % 2);
            MemoryStream stream = new MemoryStream();
            WriteS7JobHeader(stream, 14, dataLength);
            stream.WriteByte(0x05);
            stream.WriteByte(0x01);
            WriteAddressItem(stream, address, data.Length);
            stream.WriteByte(0x00);
            stream.WriteByte(0x04);
            WriteUInt16(stream, (ushort)(data.Length * 8));
            stream.Write(data, 0, data.Length);
            if ((data.Length % 2) != 0)
                stream.WriteByte(0x00);
            return stream.ToArray();
        }

        private byte[] BuildBitWriteRequest(S7Address address, byte[] values, int bitCount)
        {
            if (values == null || bitCount <= 0 || values.Length < bitCount)
                throw new ArgumentException("S7 bit write values are invalid.", nameof(values));

            int packedLength = (bitCount + 7) / 8;
            byte[] packed = new byte[packedLength];
            for (int i = 0; i < bitCount; i++)
            {
                if (values[i] != 0)
                    packed[i / 8] |= (byte)(1 << (i % 8));
            }

            int dataLength = 4 + packed.Length + (packed.Length % 2);
            MemoryStream stream = new MemoryStream();
            WriteS7JobHeader(stream, 14, dataLength);
            stream.WriteByte(0x05);
            stream.WriteByte(0x01);
            WriteAddressItem(stream, address, bitCount, true);
            stream.WriteByte(0x00);
            stream.WriteByte(0x03); // BIT transport size
            WriteUInt16(stream, (ushort)bitCount);
            stream.Write(packed, 0, packed.Length);
            if ((packed.Length % 2) != 0)
                stream.WriteByte(0x00);
            return stream.ToArray();
        }

        private void WriteS7JobHeader(Stream stream, int parameterLength, int dataLength)
        {
            ushort seq = NextSequence();
            stream.WriteByte(0x32);
            stream.WriteByte(0x01);
            stream.WriteByte(0x00);
            stream.WriteByte(0x00);
            WriteUInt16(stream, seq);
            WriteUInt16(stream, (ushort)parameterLength);
            WriteUInt16(stream, (ushort)dataLength);
        }

        private void WriteAddressItem(Stream stream, S7Address address, int byteCount)
        {
            WriteAddressItem(stream, address, byteCount, false);
        }

        private void WriteAddressItem(Stream stream, S7Address address, int amount, bool bitTransport)
        {
            int bitAddress = address.ByteOffset * 8 + (bitTransport ? address.BitOffset : 0);
            stream.WriteByte(0x12);
            stream.WriteByte(0x0A);
            stream.WriteByte(0x10);
            stream.WriteByte(bitTransport ? (byte)0x01 : (byte)0x02);
            WriteUInt16(stream, (ushort)amount);
            WriteUInt16(stream, address.DbNumber);
            stream.WriteByte(address.Area);
            stream.WriteByte((byte)((bitAddress >> 16) & 0xFF));
            stream.WriteByte((byte)((bitAddress >> 8) & 0xFF));
            stream.WriteByte((byte)(bitAddress & 0xFF));
        }

        private byte[] SendS7(byte[] s7Payload)
        {
            if (_stream == null)
                throw new InvalidOperationException("PLC 尚未连接。");

            byte[] packet = WrapS7(s7Payload);
            _stream.Write(packet, 0, packet.Length);
            return ReadTpkt();
        }

        private async ValueTask<byte[]> SendS7Async(
            byte[] s7Payload,
            CancellationToken cancellationToken)
        {
            if (_stream == null)
                throw new InvalidOperationException("PLC is not connected.");

            byte[] packet = WrapS7(s7Payload);
            await _stream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
            return await ReadTpktAsync(cancellationToken).ConfigureAwait(false);
        }

        private byte[] WrapS7(byte[] s7Payload)
        {
            int length = s7Payload.Length + 7;
            byte[] packet = new byte[length];
            packet[0] = 0x03;
            packet[1] = 0x00;
            packet[2] = (byte)(length >> 8);
            packet[3] = (byte)(length & 0xFF);
            packet[4] = 0x02;
            packet[5] = 0xF0;
            packet[6] = 0x80;
            Buffer.BlockCopy(s7Payload, 0, packet, 7, s7Payload.Length);
            return packet;
        }

        private byte[] ParseReadResponse(byte[] response)
        {
            int s7 = GetS7Offset(response);
            EnsureAckData(response, s7);
            int parameterLength = ReadUInt16(response, s7 + 6);
            int dataOffset = s7 + 12 + parameterLength;
            if (response.Length < dataOffset + 4)
                throw new InvalidOperationException("S7 读取响应过短。");

            byte returnCode = response[dataOffset];
            if (returnCode != 0xFF)
                throw S7ProtocolErrors.Item(returnCode, "read");
            if (false && returnCode != 0xFF)
                throw new InvalidOperationException("S7 读取失败，返回码: 0x" + returnCode.ToString("X2"));

            int bitLength = ReadUInt16(response, dataOffset + 2);
            int byteLength = (bitLength + 7) / 8;
            if (response.Length < dataOffset + 4 + byteLength)
                throw new InvalidOperationException("S7 读取数据长度不足。");

            return Slice(response, dataOffset + 4, byteLength);
        }

        internal void ParseMultiReadResponse(byte[] response, IList<S7MultiReadItem> items)
        {
            int s7 = GetS7Offset(response);
            EnsureAckData(response, s7);
            int parameterLength = ReadUInt16(response, s7 + 6);
            int dataOffset = s7 + 12 + parameterLength;

            for (int i = 0; i < items.Count; i++)
            {
                if (response.Length < dataOffset + 4)
                    throw new InvalidOperationException("S7 multi-read response is shorter than expected.");

                byte returnCode = response[dataOffset];
                byte transportSize = response[dataOffset + 1];
                int encodedLength = ReadUInt16(response, dataOffset + 2);
                int byteLength = transportSize == 0x09 ? encodedLength : (encodedLength + 7) / 8;
                if (response.Length < dataOffset + 4 + byteLength)
                    throw new InvalidOperationException("S7 multi-read item length is invalid.");

                S7MultiReadItem item = items[i];
                if (returnCode == 0xFF)
                {
                    item.Data = Slice(response, dataOffset + 4, byteLength);
                    item.ErrorMessage = string.Empty;
                }
                else
                {
                    item.Data = new byte[0];
                    item.ErrorMessage = GetS7ItemError(returnCode);
                    item.FailureScope = returnCode == 0x01
                        ? PlcReadFailureScope.Device
                        : PlcReadFailureScope.Tag;
                }

                dataOffset += 4 + byteLength;
                if ((byteLength % 2) != 0 && i < items.Count - 1)
                    dataOffset++;
            }
        }

        private static string GetS7ItemError(byte returnCode)
        {
            return "S7 item read failed: " + S7ProtocolErrors.Describe(returnCode);
        }

        private void ParseWriteResponse(byte[] response)
        {
            int s7 = GetS7Offset(response);
            EnsureAckData(response, s7);
            int parameterLength = ReadUInt16(response, s7 + 6);
            int dataOffset = s7 + 12 + parameterLength;
            if (response.Length < dataOffset + 1)
                throw new InvalidOperationException("S7 写入响应过短。");

            byte returnCode = response[dataOffset];
            if (returnCode != 0xFF)
                throw S7ProtocolErrors.Item(returnCode, "write");
            if (false && returnCode != 0xFF)
                throw new InvalidOperationException("S7 写入失败，返回码: 0x" + returnCode.ToString("X2"));
        }

        private void EnsureAckData(byte[] response, int s7)
        {
            if (response.Length < s7 + 12 || response[s7] != 0x32)
                throw new InvalidOperationException("S7 响应格式无效。");
            if (response[s7 + 1] != 0x03)
                throw new InvalidOperationException("S7 响应类型无效: 0x" + response[s7 + 1].ToString("X2"));

            byte errorClass = response[s7 + 10];
            byte errorCode = response[s7 + 11];
            if (errorClass != 0 || errorCode != 0)
                throw S7ProtocolErrors.Ack(errorClass, errorCode);
            if (false && (errorClass != 0 || errorCode != 0))
                throw new InvalidOperationException("S7 响应错误: class 0x" + errorClass.ToString("X2") + ", code 0x" + errorCode.ToString("X2"));
        }

        private int GetS7Offset(byte[] packet)
        {
            if (packet.Length < 7 || packet[0] != 0x03)
                throw new InvalidOperationException("TPKT 响应格式无效。");
            return 7;
        }

        private byte[] ReadTpkt()
        {
            byte[] header = ReadExact(4);
            if (header[0] != 0x03)
                throw new InvalidOperationException("TPKT 版本无效。");
            int length = (header[2] << 8) | header[3];
            if (length < 4)
                throw new InvalidOperationException("TPKT 长度无效。");

            byte[] packet = new byte[length];
            Buffer.BlockCopy(header, 0, packet, 0, 4);
            byte[] rest = ReadExact(length - 4);
            Buffer.BlockCopy(rest, 0, packet, 4, rest.Length);
            return packet;
        }

        private async ValueTask<byte[]> ReadTpktAsync(CancellationToken cancellationToken)
        {
            byte[] header = await ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
            if (header[0] != 0x03)
                throw new InvalidOperationException("TPKT version is invalid.");
            int length = (header[2] << 8) | header[3];
            if (length < 4)
                throw new InvalidOperationException("TPKT length is invalid.");

            byte[] packet = new byte[length];
            Buffer.BlockCopy(header, 0, packet, 0, 4);
            byte[] rest = await ReadExactAsync(length - 4, cancellationToken).ConfigureAwait(false);
            Buffer.BlockCopy(rest, 0, packet, 4, rest.Length);
            return packet;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("连接已关闭。");
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
                    throw new IOException("Connection was closed.");
                offset += read;
            }
            return buffer;
        }

        private ushort NextSequence()
        {
            _sequence++;
            if (_sequence == 0)
                _sequence = 1;
            return _sequence;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            if (data.Length < offset + 2)
                throw new InvalidOperationException("数据长度不足。");
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        private static string GetTypeName(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.BoolArray:
                    return "BOOL";
                case PlcDataType.Int16:
                case PlcDataType.Int16Array:
                    return "INT";
                case PlcDataType.UInt16:
                case PlcDataType.UInt16Array:
                    return "UINT";
                case PlcDataType.Int32:
                case PlcDataType.Int32Array:
                    return "DINT";
                case PlcDataType.UInt32:
                case PlcDataType.UInt32Array:
                    return "UDINT";
                case PlcDataType.Int64:
                case PlcDataType.Int64Array:
                    return "LINT";
                case PlcDataType.UInt64:
                case PlcDataType.UInt64Array:
                    return "ULINT";
                case PlcDataType.String:
                    return "STRING";
                case PlcDataType.Float:
                case PlcDataType.FloatArray:
                    return "REAL";
                case PlcDataType.Double:
                case PlcDataType.DoubleArray:
                    return "LREAL";
                default:
                    return dataType.ToString();
            }
        }
    }
}
