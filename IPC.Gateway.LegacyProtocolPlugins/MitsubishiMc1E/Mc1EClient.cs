/*----------------------------------------------------------------
* 椤圭洰鍚嶇О 锛欼PC.Plc.Communication.MitsubishiMc1E
* 椤圭洰鎻忚堪 锛?* 绫?鍚?绉?锛歁c1EClient
* 绫?鎻?杩?锛?* 鎵€鍦ㄧ殑鍩?锛?* 鍛藉悕绌洪棿 锛欼PC.Plc.Communication.MitsubishiMc1E
* 鏈哄櫒鍚嶇О 锛歎NKNOWN 
* CLR 鐗堟湰 锛?0.0.0
* 浣?   鑰?锛歩pc
* 鍒涘缓鏃堕棿 锛?026-06-23 17:52:06
* 鏇存柊鏃堕棿 锛?026-06-23 17:52:06
* 鐗?鏈?鍙?锛歷1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Gateway.LegacyProtocolPlugins.Mitsubishi;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Plc.Communication.MitsubishiMc1E
{
    
    
    
    
    
    
    
    
    
    public sealed class Mc1EClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const int MaxWordPoints = 64;
        private const int MaxBitPoints = 160;

        private readonly PlcConnectionOptions _options;
        private readonly int _maxBatchGapPoints;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private UdpClient _udpClient;
        private IPEndPoint _udpRemoteEndPoint;
        private bool _udpCommunicationConfirmed;

        public Mc1EClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            _options = options;
            _maxBatchGapPoints = McDriverOptions.Parse(options).MaxBatchGapPoints;
            if (_options.Port <= 0)
                _options.Port = 5000;
        }

        public bool IsConnected
        {
            get
            {
                if (_options.Transport == NetworkTransport.Udp)
                    return _udpClient != null && _udpCommunicationConfirmed;
                return _tcpClient != null && _tcpClient.Connected && _stream != null;
            }
        }

        public PlcProtocol Protocol { get { return PlcProtocol.MitsubishiMc1E; } }

        public void Connect()
        {
            if (_options.Transport == NetworkTransport.Udp && _udpClient != null)
                return;

            if (IsConnected)
                return;

            if (_options.Transport == NetworkTransport.Udp)
            {
                IPAddress[] addresses = Dns.GetHostAddresses(_options.Host);
                if (addresses == null || addresses.Length == 0)
                    throw new InvalidOperationException("PLC 地址验证失败: " + _options.Host);

                _udpRemoteEndPoint = new IPEndPoint(addresses[0], _options.Port);
                _udpCommunicationConfirmed = false;
                _udpClient = new UdpClient();
                _udpClient.Client.ReceiveTimeout = _options.TimeoutMilliseconds;
                _udpClient.Client.SendTimeout = _options.TimeoutMilliseconds;
                _udpClient.Connect(_udpRemoteEndPoint);
                return;
            }

            _tcpClient = new TcpClient();
            IAsyncResult async = _tcpClient.BeginConnect(_options.Host, _options.Port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(_options.TimeoutMilliseconds))
                throw new TimeoutException("连接 Mitsubishi MC 1E PLC 超时");

            _tcpClient.EndConnect(async);
            _tcpClient.ReceiveTimeout = _options.TimeoutMilliseconds;
            _tcpClient.SendTimeout = _options.TimeoutMilliseconds;
            _stream = _tcpClient.GetStream();
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (_options.Transport == NetworkTransport.Udp && _udpClient != null)
                return;

            if (IsConnected)
                return;

            if (_options.Transport == NetworkTransport.Udp)
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(_options.Host, cancellationToken).ConfigureAwait(false);
                if (addresses == null || addresses.Length == 0)
                    throw new InvalidOperationException("PLC address resolution failed: " + _options.Host);

                _udpRemoteEndPoint = new IPEndPoint(addresses[0], _options.Port);
                _udpCommunicationConfirmed = false;
                _udpClient = new UdpClient();
                _udpClient.Client.ReceiveTimeout = _options.TimeoutMilliseconds;
                _udpClient.Client.SendTimeout = _options.TimeoutMilliseconds;
                _udpClient.Connect(_udpRemoteEndPoint);
                return;
            }

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);
            _tcpClient.ReceiveTimeout = _options.TimeoutMilliseconds;
            _tcpClient.SendTimeout = _options.TimeoutMilliseconds;
            _stream = _tcpClient.GetStream();
        }

        public void Disconnect()
        {
            if (_stream != null)
                _stream.Dispose();
            if (_tcpClient != null)
                _tcpClient.Close();
            if (_udpClient != null)
                _udpClient.Close();
            _stream = null;
            _tcpClient = null;
            _udpClient = null;
            _udpRemoteEndPoint = null;
            _udpCommunicationConfirmed = false;
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

            Mc1EAddress mcAddress = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.Bool)
                return ReadBool(mcAddress);
            if (dataType == PlcDataType.BoolArray)
                return ReadBoolArray(mcAddress, elementCount);

            int wordCount = McDataCodec.GetWordCount(dataType, elementCount);
            byte[] data = ReadWordsSegmented(mcAddress, wordCount);
            object value = McDataCodec.Decode(dataType, data, elementCount);
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

            Mc1EAddress mcAddress = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.Bool)
                return await ReadBoolAsync(mcAddress, cancellationToken).ConfigureAwait(false);
            if (dataType == PlcDataType.BoolArray)
                return await ReadBoolArrayAsync(mcAddress, elementCount, cancellationToken).ConfigureAwait(false);

            int wordCount = McDataCodec.GetWordCount(dataType, elementCount);
            byte[] data = await ReadWordsSegmentedAsync(mcAddress, wordCount, cancellationToken).ConfigureAwait(false);
            object value = McDataCodec.Decode(dataType, data, elementCount);
            return new PlcReadResult(0, GetTypeName(dataType), value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            return MitsubishiBatchReadExecutor.ReadMany(requests, new MitsubishiBatchReadContext<Mc1EAddress>
            {
                BuildAddress = BuildAddress,
                GetAreaKey = delegate(Mc1EAddress address)
                {
                    return address.DeviceName + "|" + address.Code1.ToString("X2") + address.Code2.ToString("X2") + "|" + address.IsBitDevice;
                },
                GetDeviceNumber = delegate(Mc1EAddress address) { return address.DeviceNumber; },
                GetBitOffset = delegate(Mc1EAddress address) { return address.BitOffset; },
                IsBitDevice = delegate(Mc1EAddress address) { return address.IsBitDevice; },
                AddDeviceOffset = delegate(Mc1EAddress address, int offset) { return address.AddDeviceOffset(offset); },
                ReadWords = ReadWordsSegmented,
                ReadBits = delegate(Mc1EAddress address, int count)
                {
                    return McDataCodec.UnpackBits(ReadBitsSegmented(address, count), count);
                },
                MaxWordPoints = MaxWordPoints,
                MaxBitPoints = MaxBitPoints,
                MaxGapPoints = _maxBatchGapPoints,
                GetTypeName = GetTypeName
            });
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            return await MitsubishiBatchReadExecutor.ReadManyAsync(requests, new MitsubishiAsyncBatchReadContext<Mc1EAddress>
            {
                BuildAddress = BuildAddress,
                GetAreaKey = delegate(Mc1EAddress address)
                {
                    return address.DeviceName + "|" + address.Code1.ToString("X2") + address.Code2.ToString("X2") + "|" + address.IsBitDevice;
                },
                GetDeviceNumber = delegate(Mc1EAddress address) { return address.DeviceNumber; },
                GetBitOffset = delegate(Mc1EAddress address) { return address.BitOffset; },
                IsBitDevice = delegate(Mc1EAddress address) { return address.IsBitDevice; },
                AddDeviceOffset = delegate(Mc1EAddress address, int offset) { return address.AddDeviceOffset(offset); },
                ReadWordsAsync = ReadWordsSegmentedAsync,
                ReadBitsAsync = async delegate(Mc1EAddress address, int count, CancellationToken token)
                {
                    return McDataCodec.UnpackBits(await ReadBitsSegmentedAsync(address, count, token).ConfigureAwait(false), count);
                },
                MaxWordPoints = MaxWordPoints,
                MaxBitPoints = MaxBitPoints,
                MaxGapPoints = _maxBatchGapPoints,
                GetTypeName = GetTypeName
            }, cancellationToken).ConfigureAwait(false);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            int defaultElementCount = dataType == PlcDataType.String ? McDataCodec.DefaultStringBytes : 1;
            Write(address, dataType, valueText, defaultElementCount, elementOffset);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementCount, int elementOffset)
        {
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            int writeElementCount = McDataCodec.GetElementCount(dataType, valueText, elementCount);
            Mc1EAddress mcAddress = BuildAddress(address, dataType, elementOffset);
            byte[] data = McDataCodec.Encode(dataType, valueText, writeElementCount);

            if (dataType == PlcDataType.Bool)
            {
                WriteBool(mcAddress, data[0] != 0);
                return;
            }

            if (dataType == PlcDataType.BoolArray)
            {
                WriteBoolArray(mcAddress, data, writeElementCount);
                return;
            }

            int wordCount = GetWordCountForWrite(data);
            PreserveOddStringTailByte(dataType, writeElementCount, mcAddress, data, wordCount);
            WriteWordsSegmented(mcAddress, data, wordCount);
        }

        public async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            int defaultElementCount = dataType == PlcDataType.String ? McDataCodec.DefaultStringBytes : 1;
            await WriteAsync(address, dataType, valueText, defaultElementCount, elementOffset, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            int writeElementCount = McDataCodec.GetElementCount(dataType, valueText, elementCount);
            Mc1EAddress mcAddress = BuildAddress(address, dataType, elementOffset);
            byte[] data = McDataCodec.Encode(dataType, valueText, writeElementCount);

            if (dataType == PlcDataType.Bool)
            {
                await WriteBoolAsync(mcAddress, data[0] != 0, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (dataType == PlcDataType.BoolArray)
            {
                await WriteBoolArrayAsync(mcAddress, data, writeElementCount, cancellationToken).ConfigureAwait(false);
                return;
            }

            int wordCount = GetWordCountForWrite(data);
            await PreserveOddStringTailByteAsync(dataType, writeElementCount, mcAddress, data, wordCount, cancellationToken).ConfigureAwait(false);
            await WriteWordsSegmentedAsync(mcAddress, data, wordCount, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private Mc1EAddress BuildAddress(string address, PlcDataType dataType, int elementOffset)
        {
            Mc1EAddress parsed = Mc1EAddress.Parse(address);
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray)
                return parsed.AddBitOffset(elementOffset);

            return parsed.AddDeviceOffset(McDataCodec.GetDeviceOffset(dataType, elementOffset));
        }

        private PlcReadResult ReadBool(Mc1EAddress address)
        {
            bool value;
            if (address.IsBitDevice)
            {
                byte[] packed = ReadBits(address, 1);
                value = McDataCodec.UnpackBits(packed, 1)[0];
            }
            else
            {
                byte[] word = ReadWordsSegmented(address, 1);
                value = (BitConverter.ToUInt16(word, 0) & (1 << address.BitOffset)) != 0;
            }

            return new PlcReadResult(0, "BOOL", value);
        }

        private async ValueTask<PlcReadResult> ReadBoolAsync(
            Mc1EAddress address,
            CancellationToken cancellationToken)
        {
            bool value;
            if (address.IsBitDevice)
            {
                byte[] packed = await ReadBitsAsync(address, 1, cancellationToken).ConfigureAwait(false);
                value = McDataCodec.UnpackBits(packed, 1)[0];
            }
            else
            {
                byte[] word = await ReadWordsSegmentedAsync(address, 1, cancellationToken).ConfigureAwait(false);
                value = (BitConverter.ToUInt16(word, 0) & (1 << address.BitOffset)) != 0;
            }

            return new PlcReadResult(0, "BOOL", value);
        }

        private PlcReadResult ReadBoolArray(Mc1EAddress address, int count)
        {
            bool[] values;
            if (address.IsBitDevice)
            {
                byte[] packed = ReadBitsSegmented(address, count);
                values = McDataCodec.UnpackBits(packed, count);
            }
            else
            {
                int wordCount = (address.BitOffset + count + 15) / 16;
                byte[] words = ReadWordsSegmented(address, wordCount);
                values = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    int absoluteBit = address.BitOffset + i;
                    int wordIndex = absoluteBit / 16;
                    int bitIndex = absoluteBit % 16;
                    values[i] = (BitConverter.ToUInt16(words, wordIndex * 2) & (1 << bitIndex)) != 0;
                }
            }

            return new PlcReadResult(0, "BOOL", values);
        }

        private async ValueTask<PlcReadResult> ReadBoolArrayAsync(
            Mc1EAddress address,
            int count,
            CancellationToken cancellationToken)
        {
            bool[] values;
            if (address.IsBitDevice)
            {
                byte[] packed = await ReadBitsSegmentedAsync(address, count, cancellationToken).ConfigureAwait(false);
                values = McDataCodec.UnpackBits(packed, count);
            }
            else
            {
                int wordCount = (address.BitOffset + count + 15) / 16;
                byte[] words = await ReadWordsSegmentedAsync(address, wordCount, cancellationToken).ConfigureAwait(false);
                values = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    int absoluteBit = address.BitOffset + i;
                    int wordIndex = absoluteBit / 16;
                    int bitIndex = absoluteBit % 16;
                    values[i] = (BitConverter.ToUInt16(words, wordIndex * 2) & (1 << bitIndex)) != 0;
                }
            }

            return new PlcReadResult(0, "BOOL", values);
        }

        private void WriteBool(Mc1EAddress address, bool value)
        {
            if (address.IsBitDevice)
            {
                WriteBits(address, McDataCodec.PackBits(new[] { value ? (byte)1 : (byte)0 }, 1), 1);
                return;
            }

            byte[] word = ReadWordsSegmented(address, 1);
            McDataCodec.SetWordBit(word, address.BitOffset, value);
            WriteWordsSegmented(address, word, 1);
        }

        private async ValueTask WriteBoolAsync(
            Mc1EAddress address,
            bool value,
            CancellationToken cancellationToken)
        {
            if (address.IsBitDevice)
            {
                await WriteBitsAsync(address, McDataCodec.PackBits(new[] { value ? (byte)1 : (byte)0 }, 1), 1, cancellationToken).ConfigureAwait(false);
                return;
            }

            byte[] word = await ReadWordsSegmentedAsync(address, 1, cancellationToken).ConfigureAwait(false);
            McDataCodec.SetWordBit(word, address.BitOffset, value);
            await WriteWordsSegmentedAsync(address, word, 1, cancellationToken).ConfigureAwait(false);
        }

        private void WriteBoolArray(Mc1EAddress address, byte[] values, int count)
        {
            if (address.IsBitDevice)
            {
                WriteBitsSegmented(address, values, count);
                return;
            }

            int wordCount = (address.BitOffset + count + 15) / 16;
            byte[] words = ReadWordsSegmented(address, wordCount);
            for (int i = 0; i < count; i++)
            {
                int absoluteBit = address.BitOffset + i;
                int wordIndex = absoluteBit / 16;
                int bitIndex = absoluteBit % 16;
                byte[] word = new byte[2];
                Buffer.BlockCopy(words, wordIndex * 2, word, 0, 2);
                McDataCodec.SetWordBit(word, bitIndex, values[i] != 0);
                Buffer.BlockCopy(word, 0, words, wordIndex * 2, 2);
            }

            WriteWordsSegmented(address, words, wordCount);
        }

        private async ValueTask WriteBoolArrayAsync(
            Mc1EAddress address,
            byte[] values,
            int count,
            CancellationToken cancellationToken)
        {
            if (address.IsBitDevice)
            {
                await WriteBitsSegmentedAsync(address, values, count, cancellationToken).ConfigureAwait(false);
                return;
            }

            int wordCount = (address.BitOffset + count + 15) / 16;
            byte[] words = await ReadWordsSegmentedAsync(address, wordCount, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < count; i++)
            {
                int absoluteBit = address.BitOffset + i;
                int wordIndex = absoluteBit / 16;
                int bitIndex = absoluteBit % 16;
                byte[] word = new byte[2];
                Buffer.BlockCopy(words, wordIndex * 2, word, 0, 2);
                McDataCodec.SetWordBit(word, bitIndex, values[i] != 0);
                Buffer.BlockCopy(word, 0, words, wordIndex * 2, 2);
            }

            await WriteWordsSegmentedAsync(address, words, wordCount, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadWordsSegmented(Mc1EAddress address, int wordCount)
        {
            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < wordCount)
            {
                int chunk = Math.Min(MaxWordPoints, wordCount - offset);
                byte[] data = ReadWords(address.AddDeviceOffset(offset), chunk);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }
            return result.ToArray();
        }

        private async ValueTask<byte[]> ReadWordsSegmentedAsync(
            Mc1EAddress address,
            int wordCount,
            CancellationToken cancellationToken)
        {
            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < wordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(MaxWordPoints, wordCount - offset);
                byte[] data = await ReadWordsAsync(address.AddDeviceOffset(offset), chunk, cancellationToken).ConfigureAwait(false);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }
            return result.ToArray();
        }

        private void WriteWordsSegmented(Mc1EAddress address, byte[] data, int wordCount)
        {
            int offset = 0;
            while (offset < wordCount)
            {
                int chunk = Math.Min(MaxWordPoints, wordCount - offset);
                byte[] chunkData = new byte[chunk * 2];
                Buffer.BlockCopy(data, offset * 2, chunkData, 0, chunkData.Length);
                WriteWords(address.AddDeviceOffset(offset), chunkData, chunk);
                offset += chunk;
            }
        }

        private async ValueTask WriteWordsSegmentedAsync(
            Mc1EAddress address,
            byte[] data,
            int wordCount,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < wordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(MaxWordPoints, wordCount - offset);
                byte[] chunkData = new byte[chunk * 2];
                Buffer.BlockCopy(data, offset * 2, chunkData, 0, chunkData.Length);
                await WriteWordsAsync(address.AddDeviceOffset(offset), chunkData, chunk, cancellationToken).ConfigureAwait(false);
                offset += chunk;
            }
        }

        private byte[] ReadBitsSegmented(Mc1EAddress address, int count)
        {
            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < count)
            {
                int chunk = Math.Min(MaxBitPoints, count - offset);
                byte[] data = ReadBits(address.AddDeviceOffset(offset), chunk);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }
            return result.ToArray();
        }

        private async ValueTask<byte[]> ReadBitsSegmentedAsync(
            Mc1EAddress address,
            int count,
            CancellationToken cancellationToken)
        {
            MemoryStream result = new MemoryStream();
            int offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(MaxBitPoints, count - offset);
                byte[] data = await ReadBitsAsync(address.AddDeviceOffset(offset), chunk, cancellationToken).ConfigureAwait(false);
                result.Write(data, 0, data.Length);
                offset += chunk;
            }
            return result.ToArray();
        }

        private void WriteBitsSegmented(Mc1EAddress address, byte[] values, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int chunk = Math.Min(MaxBitPoints, count - offset);
                byte[] chunkValues = new byte[chunk];
                Buffer.BlockCopy(values, offset, chunkValues, 0, chunk);
                WriteBits(address.AddDeviceOffset(offset), McDataCodec.PackBits(chunkValues, chunk), chunk);
                offset += chunk;
            }
        }

        private async ValueTask WriteBitsSegmentedAsync(
            Mc1EAddress address,
            byte[] values,
            int count,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(MaxBitPoints, count - offset);
                byte[] chunkValues = new byte[chunk];
                Buffer.BlockCopy(values, offset, chunkValues, 0, chunk);
                await WriteBitsAsync(address.AddDeviceOffset(offset), McDataCodec.PackBits(chunkValues, chunk), chunk, cancellationToken).ConfigureAwait(false);
                offset += chunk;
            }
        }

        private byte[] ReadWords(Mc1EAddress address, int points)
        {
            return Send(BuildRequest(0x01, address, points, null), 0x81);
        }

        private async ValueTask<byte[]> ReadWordsAsync(
            Mc1EAddress address,
            int points,
            CancellationToken cancellationToken)
        {
            return await SendAsync(BuildRequest(0x01, address, points, null), 0x81, cancellationToken).ConfigureAwait(false);
        }

        private void WriteWords(Mc1EAddress address, byte[] data, int points)
        {
            Send(BuildRequest(0x03, address, points, data), 0x83);
        }

        private async ValueTask WriteWordsAsync(
            Mc1EAddress address,
            byte[] data,
            int points,
            CancellationToken cancellationToken)
        {
            await SendAsync(BuildRequest(0x03, address, points, data), 0x83, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadBits(Mc1EAddress address, int points)
        {
            return Send(BuildRequest(0x00, address, points, null), 0x80);
        }

        private async ValueTask<byte[]> ReadBitsAsync(
            Mc1EAddress address,
            int points,
            CancellationToken cancellationToken)
        {
            return await SendAsync(BuildRequest(0x00, address, points, null), 0x80, cancellationToken).ConfigureAwait(false);
        }

        private void WriteBits(Mc1EAddress address, byte[] packedBits, int points)
        {
            Send(BuildRequest(0x02, address, points, packedBits), 0x82);
        }

        private async ValueTask WriteBitsAsync(
            Mc1EAddress address,
            byte[] packedBits,
            int points,
            CancellationToken cancellationToken)
        {
            await SendAsync(BuildRequest(0x02, address, points, packedBits), 0x82, cancellationToken).ConfigureAwait(false);
        }

        private byte[] BuildRequest(byte command, Mc1EAddress address, int points, byte[] data)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte(command);
            stream.WriteByte(0xFF);
            WriteUInt16(stream, 0x000A);
            WriteUInt32(stream, (uint)address.DeviceNumber);
            stream.WriteByte(address.Code1);
            stream.WriteByte(address.Code2);
            WriteUInt16(stream, (ushort)points);
            if (data != null && data.Length > 0)
                stream.Write(data, 0, data.Length);
            return stream.ToArray();
        }

        private byte[] Send(byte[] request, byte expectedResponse)
        {
            try
            {
            if (!IsConnected)
                Connect();

            byte[] response;
            if (_options.Transport == NetworkTransport.Udp)
            {
                try
                {
                    DrainPendingUdpResponses();
                    _udpClient.Send(request, request.Length);
                    IPEndPoint remote = null;
                    response = _udpClient.Receive(ref remote);
                    ValidateUdpRemoteEndPoint(remote);
                }
                catch
                {
                    _udpCommunicationConfirmed = false;
                    throw;
                }
            }
            else
            {
                _stream.Write(request, 0, request.Length);
                byte[] header = ReadExact(2);
                if (header[0] != expectedResponse)
                    throw McProtocolErrors.Frame("MC 1E response frame type is invalid: 0x" + header[0].ToString("X2"));
                if (header[1] == 0)
                    return ReadRemainingData(expectedResponse, request);
                if (header[1] == 0x5B)
                {
                    byte[] detail = ReadExact(2);
                    throw McProtocolErrors.Mc1E(0x5B, (ushort)((detail[0] << 8) | detail[1]));
                }
                throw McProtocolErrors.Mc1E(header[1]);
            }

            if (response == null || response.Length < 2)
                throw new InvalidOperationException("MC 1E 响应数据无效");
            if (response[0] != expectedResponse)
                throw McProtocolErrors.Frame("MC 1E response frame type is invalid: 0x" + response[0].ToString("X2"));
            if (response[1] == 0)
            {
                if (_options.Transport == NetworkTransport.Udp)
                    _udpCommunicationConfirmed = true;

                byte[] data = new byte[response.Length - 2];
                Buffer.BlockCopy(response, 2, data, 0, data.Length);
                return data;
            }
            if (response[1] == 0x5B)
            {
                if (response.Length < 4)
                    throw new InvalidOperationException("MC 1E 错误响应数据无效");
                throw McProtocolErrors.Mc1E(0x5B, (ushort)((response[2] << 8) | response[3]));
            }

            throw McProtocolErrors.Mc1E(response[1]);
            }
            catch
            {
                if (_options.Transport == NetworkTransport.Udp)
                    _udpCommunicationConfirmed = false;
                throw;
            }
        }

        private async ValueTask<byte[]> SendAsync(
            byte[] request,
            byte expectedResponse,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!IsConnected)
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);

                byte[] response;
                if (_options.Transport == NetworkTransport.Udp)
                {
                    try
                    {
                        DrainPendingUdpResponses();
                        using CancellationTokenSource udpTimeout = CreateUdpOperationCancellationTokenSource(cancellationToken);
                        await _udpClient.SendAsync(request, request.Length).WaitAsync(udpTimeout.Token).ConfigureAwait(false);
                        UdpReceiveResult receiveResult = await _udpClient.ReceiveAsync(udpTimeout.Token).ConfigureAwait(false);
                        response = receiveResult.Buffer;
                        ValidateUdpRemoteEndPoint(receiveResult.RemoteEndPoint);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        _udpCommunicationConfirmed = false;
                        throw new TimeoutException("Mitsubishi MC1E UDP request timed out.", ex);
                    }
                    catch
                    {
                        _udpCommunicationConfirmed = false;
                        throw;
                    }
                }
                else
                {
                    await _stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);
                    byte[] header = await ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
                    if (header[0] != expectedResponse)
                        throw McProtocolErrors.Frame("MC 1E response frame type is invalid: 0x" + header[0].ToString("X2"));
                    if (header[1] == 0)
                        return await ReadRemainingDataAsync(expectedResponse, request, cancellationToken).ConfigureAwait(false);
                    if (header[1] == 0x5B)
                    {
                        byte[] detail = await ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
                        throw McProtocolErrors.Mc1E(0x5B, (ushort)((detail[0] << 8) | detail[1]));
                    }
                    throw McProtocolErrors.Mc1E(header[1]);
                }

                if (response == null || response.Length < 2)
                    throw new InvalidOperationException("MC 1E response data is invalid.");
                if (response[0] != expectedResponse)
                    throw McProtocolErrors.Frame("MC 1E response frame type is invalid: 0x" + response[0].ToString("X2"));
                if (response[1] == 0)
                {
                    if (_options.Transport == NetworkTransport.Udp)
                        _udpCommunicationConfirmed = true;

                    byte[] data = new byte[response.Length - 2];
                    Buffer.BlockCopy(response, 2, data, 0, data.Length);
                    return data;
                }
                if (response[1] == 0x5B)
                {
                    if (response.Length < 4)
                        throw new InvalidOperationException("MC 1E error response data is invalid.");
                    throw McProtocolErrors.Mc1E(0x5B, (ushort)((response[2] << 8) | response[3]));
                }

                throw McProtocolErrors.Mc1E(response[1]);
            }
            catch
            {
                if (_options.Transport == NetworkTransport.Udp)
                    _udpCommunicationConfirmed = false;
                throw;
            }
        }

        private CancellationTokenSource CreateUdpOperationCancellationTokenSource(CancellationToken cancellationToken)
        {
            int timeout = _options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds;
            CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            return timeoutCancellation;
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
            if (_udpRemoteEndPoint == null || remote == null)
                throw new IOException("UDP response endpoint is missing.");

            if (!_udpRemoteEndPoint.Address.Equals(remote.Address) || _udpRemoteEndPoint.Port != remote.Port)
                throw new IOException("UDP response endpoint mismatch.");
        }

        private byte[] ReadRemainingData(byte expectedResponse, byte[] request)
        {
            int length = 0;
            if (expectedResponse == 0x81)
            {
                int points = request[10] | (request[11] << 8);
                length = points * 2;
            }
            else if (expectedResponse == 0x80)
            {
                int points = request[10] | (request[11] << 8);
                length = (points + 1) / 2;
            }

            return length == 0 ? new byte[0] : ReadExact(length);
        }

        private async ValueTask<byte[]> ReadRemainingDataAsync(
            byte expectedResponse,
            byte[] request,
            CancellationToken cancellationToken)
        {
            int length = 0;
            if (expectedResponse == 0x81)
            {
                int points = request[10] | (request[11] << 8);
                length = points * 2;
            }
            else if (expectedResponse == 0x80)
            {
                int points = request[10] | (request[11] << 8);
                length = (points + 1) / 2;
            }

            return length == 0 ? new byte[0] : await ReadExactAsync(length, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("连接已关闭");
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

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }

        private static int GetWordCountForWrite(byte[] data)
        {
            return (data.Length + 1) / 2;
        }

        private void PreserveOddStringTailByte(PlcDataType dataType, int elementCount, Mc1EAddress address, byte[] data, int wordCount)
        {
            if (dataType != PlcDataType.String || (elementCount % 2) == 0 || data == null || data.Length < wordCount * 2)
                return;

            byte[] current = ReadWordsSegmented(address, wordCount);
            if (current != null && current.Length >= wordCount * 2)
                data[wordCount * 2 - 1] = current[wordCount * 2 - 1];
        }

        private async ValueTask PreserveOddStringTailByteAsync(
            PlcDataType dataType,
            int elementCount,
            Mc1EAddress address,
            byte[] data,
            int wordCount,
            CancellationToken cancellationToken)
        {
            if (dataType != PlcDataType.String || (elementCount % 2) == 0 || data == null || data.Length < wordCount * 2)
                return;

            byte[] current = await ReadWordsSegmentedAsync(address, wordCount, cancellationToken).ConfigureAwait(false);
            if (current != null && current.Length >= wordCount * 2)
                data[wordCount * 2 - 1] = current[wordCount * 2 - 1];
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
