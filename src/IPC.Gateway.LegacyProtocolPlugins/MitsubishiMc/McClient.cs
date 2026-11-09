/*----------------------------------------------------------------
* 椤圭洰鍚嶇О 锛欼PC.Plc.Communication.MitsubishiMc
* 椤圭洰鎻忚堪 锛?* 绫?鍚?绉?锛歁cClient
* 绫?鎻?杩?锛?* 鎵€鍦ㄧ殑鍩?锛?* 鍛藉悕绌洪棿 锛欼PC.Plc.Communication.MitsubishiMc
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

namespace IPC.Plc.Communication.MitsubishiMc
{
    
    
    
    
    
    
    
    
    
    public sealed class McClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const int MaxWordPoints = 480;
        private const int MaxBitPoints = 960;

        private readonly PlcConnectionOptions _options;
        private readonly IMcFrameCodec _frameCodec;
        private readonly int _maxBatchGapPoints;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private UdpClient _udpClient;
        private IPEndPoint _udpRemoteEndPoint;

        public McClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            _options = options;
            McDriverOptions driverOptions = McDriverOptions.Parse(options);
            _frameCodec = McFrameCodecFactory.Create(driverOptions);
            _maxBatchGapPoints = driverOptions.MaxBatchGapPoints;
            if (_options.Port <= 0)
                _options.Port = 5000;
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

        public PlcProtocol Protocol { get { return PlcProtocol.MitsubishiMc; } }

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
                _udpClient = new UdpClient();
                _udpClient.Client.ReceiveTimeout = _options.TimeoutMilliseconds;
                _udpClient.Client.SendTimeout = _options.TimeoutMilliseconds;
                _udpClient.Connect(_udpRemoteEndPoint);
                return;
            }

            _tcpClient = new TcpClient();
            IAsyncResult async = _tcpClient.BeginConnect(_options.Host, _options.Port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(_options.TimeoutMilliseconds))
                throw new TimeoutException("连接 Mitsubishi MC PLC 超时");

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

            McAddress mcAddress = BuildAddress(address, dataType, elementOffset);
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

            McAddress mcAddress = BuildAddress(address, dataType, elementOffset);
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
            return MitsubishiBatchReadExecutor.ReadMany(requests, new MitsubishiBatchReadContext<McAddress>
            {
                BuildAddress = BuildAddress,
                GetAreaKey = delegate(McAddress address)
                {
                    return address.DeviceName + "|" + address.DeviceCode.ToString("X2") + "|" + address.IsBitDevice;
                },
                GetDeviceNumber = delegate(McAddress address) { return address.DeviceNumber; },
                GetBitOffset = delegate(McAddress address) { return address.BitOffset; },
                IsBitDevice = delegate(McAddress address) { return address.IsBitDevice; },
                AddDeviceOffset = delegate(McAddress address, int offset) { return address.AddDeviceOffset(offset); },
                ReadWords = ReadWordsSegmented,
                ReadBits = delegate(McAddress address, int count)
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
            return await MitsubishiBatchReadExecutor.ReadManyAsync(requests, new MitsubishiAsyncBatchReadContext<McAddress>
            {
                BuildAddress = BuildAddress,
                GetAreaKey = delegate(McAddress address)
                {
                    return address.DeviceName + "|" + address.DeviceCode.ToString("X2") + "|" + address.IsBitDevice;
                },
                GetDeviceNumber = delegate(McAddress address) { return address.DeviceNumber; },
                GetBitOffset = delegate(McAddress address) { return address.BitOffset; },
                IsBitDevice = delegate(McAddress address) { return address.IsBitDevice; },
                AddDeviceOffset = delegate(McAddress address, int offset) { return address.AddDeviceOffset(offset); },
                ReadWordsAsync = ReadWordsSegmentedAsync,
                ReadBitsAsync = async delegate(McAddress address, int count, CancellationToken token)
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
            McAddress mcAddress = BuildAddress(address, dataType, elementOffset);
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
            McAddress mcAddress = BuildAddress(address, dataType, elementOffset);
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

        private McAddress BuildAddress(string address, PlcDataType dataType, int elementOffset)
        {
            McAddress parsed = McAddress.Parse(address);
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray)
                return parsed.AddBitOffset(elementOffset);

            return parsed.AddDeviceOffset(McDataCodec.GetDeviceOffset(dataType, elementOffset));
        }

        private PlcReadResult ReadBool(McAddress address)
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
            McAddress address,
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

        private PlcReadResult ReadBoolArray(McAddress address, int count)
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
            McAddress address,
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

        private void WriteBool(McAddress address, bool value)
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
            McAddress address,
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

        private void WriteBoolArray(McAddress address, byte[] values, int count)
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
            McAddress address,
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

        private byte[] ReadWordsSegmented(McAddress address, int wordCount)
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
            McAddress address,
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

        private void WriteWordsSegmented(McAddress address, byte[] data, int wordCount)
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
            McAddress address,
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

        private byte[] ReadBitsSegmented(McAddress address, int count)
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
            McAddress address,
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

        private void WriteBitsSegmented(McAddress address, byte[] values, int count)
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
            McAddress address,
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

        private byte[] ReadWords(McAddress address, int points)
        {
            byte[] response = Send(BuildRequest(0x0401, 0x0000, address, points, null), true);
            return response;
        }

        private async ValueTask<byte[]> ReadWordsAsync(
            McAddress address,
            int points,
            CancellationToken cancellationToken)
        {
            return await SendAsync(BuildRequest(0x0401, 0x0000, address, points, null), true, cancellationToken).ConfigureAwait(false);
        }

        private void WriteWords(McAddress address, byte[] data, int points)
        {
            Send(BuildRequest(0x1401, 0x0000, address, points, data), false);
        }

        private async ValueTask WriteWordsAsync(
            McAddress address,
            byte[] data,
            int points,
            CancellationToken cancellationToken)
        {
            await SendAsync(BuildRequest(0x1401, 0x0000, address, points, data), false, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadBits(McAddress address, int points)
        {
            return Send(BuildRequest(0x0401, 0x0001, address, points, null), true);
        }

        private async ValueTask<byte[]> ReadBitsAsync(
            McAddress address,
            int points,
            CancellationToken cancellationToken)
        {
            return await SendAsync(BuildRequest(0x0401, 0x0001, address, points, null), true, cancellationToken).ConfigureAwait(false);
        }

        private void WriteBits(McAddress address, byte[] packedBits, int points)
        {
            Send(BuildRequest(0x1401, 0x0001, address, points, packedBits), false);
        }

        private async ValueTask WriteBitsAsync(
            McAddress address,
            byte[] packedBits,
            int points,
            CancellationToken cancellationToken)
        {
            await SendAsync(BuildRequest(0x1401, 0x0001, address, points, packedBits), false, cancellationToken).ConfigureAwait(false);
        }

        private byte[] BuildRequest(ushort command, ushort subcommand, McAddress address, int points, byte[] data)
        {
            return _frameCodec.BuildRequest(command, subcommand, address, points, data);
        }

        private byte[] Send(byte[] request, bool allowUdpReadRetry)
        {
            if (!IsConnected)
                Connect();

            byte[] response;
            if (_options.Transport == NetworkTransport.Udp)
            {
                response = SendUdp(request, allowUdpReadRetry);
            }
            else
            {
                _stream.Write(request, 0, request.Length);
                byte[] header = ReadExact(_frameCodec.ResponseHeaderLength);
                int dataLength = _frameCodec.GetResponseDataLength(header);
                response = new byte[header.Length + dataLength];
                Buffer.BlockCopy(header, 0, response, 0, header.Length);
                byte[] body = ReadExact(dataLength);
                Buffer.BlockCopy(body, 0, response, header.Length, dataLength);
            }
            return _frameCodec.ParseResponse(response, request);
        }

        private async ValueTask<byte[]> SendAsync(
            byte[] request,
            bool allowUdpReadRetry,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);

                byte[] response;
                if (_options.Transport == NetworkTransport.Udp)
                {
                    response = await SendUdpAsync(request, allowUdpReadRetry, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);
                    byte[] header = await ReadExactAsync(_frameCodec.ResponseHeaderLength, cancellationToken).ConfigureAwait(false);
                    int dataLength = _frameCodec.GetResponseDataLength(header);
                    response = new byte[header.Length + dataLength];
                    Buffer.BlockCopy(header, 0, response, 0, header.Length);
                    byte[] body = await ReadExactAsync(dataLength, cancellationToken).ConfigureAwait(false);
                    Buffer.BlockCopy(body, 0, response, header.Length, dataLength);
                }
            return _frameCodec.ParseResponse(response, request);
        }

        private byte[] SendUdp(byte[] request, bool allowReadRetry)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    DrainPendingUdpResponses();
                    _udpClient.Send(request, request.Length);
                    IPEndPoint remote = null;
                    byte[] response = _udpClient.Receive(ref remote);
                    ValidateUdpRemoteEndPoint(remote);
                    return response;
                }
                catch (Exception ex) when (IsRetryableUdpReadFailure(ex))
                {
                    if (!allowReadRetry || attempt >= 1)
                        throw new TimeoutException("Mitsubishi MC UDP request timed out.", ex);

                    Thread.Sleep(Random.Shared.Next(25, 101));
                }
                catch
                {
                    throw;
                }
            }
        }

        private async ValueTask<byte[]> SendUdpAsync(
            byte[] request,
            bool allowReadRetry,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    DrainPendingUdpResponses();
                    using CancellationTokenSource udpTimeout = CreateUdpOperationCancellationTokenSource(cancellationToken);
                    await _udpClient.SendAsync(request, request.Length).WaitAsync(udpTimeout.Token).ConfigureAwait(false);
                    UdpReceiveResult receiveResult = await _udpClient.ReceiveAsync(udpTimeout.Token).ConfigureAwait(false);
                    ValidateUdpRemoteEndPoint(receiveResult.RemoteEndPoint);
                    return receiveResult.Buffer;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (!allowReadRetry || attempt >= 1)
                        throw new TimeoutException("Mitsubishi MC UDP request timed out.", ex);

                    await Task.Delay(Random.Shared.Next(25, 101), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryableUdpReadFailure(ex))
                {
                    if (!allowReadRetry || attempt >= 1)
                        throw new TimeoutException("Mitsubishi MC UDP request timed out.", ex);

                    await Task.Delay(Random.Shared.Next(25, 101), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    throw;
                }
            }
        }

        private static bool IsRetryableUdpReadFailure(Exception exception)
        {
            if (exception is TimeoutException)
                return true;
            if (exception is not SocketException socketException)
                return false;

            return socketException.SocketErrorCode == SocketError.TimedOut ||
                   socketException.SocketErrorCode == SocketError.WouldBlock ||
                   socketException.SocketErrorCode == SocketError.ConnectionReset ||
                   socketException.SocketErrorCode == SocketError.NetworkReset ||
                   socketException.SocketErrorCode == SocketError.HostUnreachable;
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

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("读取失败。");
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
                    throw new IOException("Read failed.");
                offset += read;
            }
            return buffer;
        }

        private static int GetWordCountForWrite(byte[] data)
        {
            return (data.Length + 1) / 2;
        }

        private void PreserveOddStringTailByte(PlcDataType dataType, int elementCount, McAddress address, byte[] data, int wordCount)
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
            McAddress address,
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
