/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Cjt188
* 项目描述 ：
* 类 名 称 ：Cjt188Client
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Cjt188
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
using IPC.Plc.Communication.Metering;

namespace IPC.Plc.Communication.Metering.Cjt188
{
    
    
    
    
    
    
    
    
    
    public sealed class Cjt188Client : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private SharedTransparentTcpLease? _channelLease;
        private NetworkStream? _stream;

        public Cjt188Client(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
        }

        public bool IsConnected
        {
            get { return _channelLease?.IsConnected == true && _stream != null; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.Cjt1882004; }
        }

        public void Connect()
        {
            Disconnect();
            if (string.IsNullOrWhiteSpace(_options.Host))
                throw new InvalidOperationException("CJ/T188-2004 当前内置驱动使用 TCP 透明传输，请配置串口服务器 IP。");

            try
            {
                _channelLease = SharedTransparentTcpChannelRegistry.Acquire(_options);
                _stream = _channelLease.Stream;
            }
            catch
            {
                _channelLease?.Dispose();
                _channelLease = null;
                _stream = null;
                throw;
            }
        }

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(_options.Host))
                throw new InvalidOperationException("CJ/T188-2004 TCP host is not configured.");

            try
            {
                _channelLease = await SharedTransparentTcpChannelRegistry.AcquireAsync(_options, cancellationToken).ConfigureAwait(false);
                _stream = _channelLease.Stream;
            }
            catch
            {
                _channelLease?.Dispose();
                _channelLease = null;
                _stream = null;
                throw;
            }
        }

        public void Disconnect()
        {
            _stream = null;
            if (_channelLease != null)
            {
                _channelLease.Dispose();
                _channelLease = null;
            }
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string addressText, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            Cjt188Address address = Cjt188Address.Parse(addressText);
            byte[] data = ReadRawData(address);
            object value = DecodeValue(address, data, dataType);
            return new PlcReadResult(0x1884, "CJ/T188-2004", value);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string addressText,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            Cjt188Address address = Cjt188Address.Parse(addressText);
            byte[] data = await ReadRawDataAsync(address, cancellationToken).ConfigureAwait(false);
            object value = DecodeValue(address, data, dataType);
            return new PlcReadResult(0x1884, "CJ/T188-2004", value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            EnsureConnected();
            return MeterBatchReadExecutor.ReadMany(requests, new MeterBatchReadContext<Cjt188Address>
            {
                ParseAddress = Cjt188Address.Parse,
                GetAddressKey = GetAddressKey,
                ReadRawData = ReadRawData,
                DecodeValue = DecodeValue,
                TypeCode = 0x1884,
                TypeName = "CJ/T188-2004"
            });
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            return await MeterBatchReadExecutor.ReadManyAsync(requests, new MeterAsyncBatchReadContext<Cjt188Address>
            {
                ParseAddress = Cjt188Address.Parse,
                GetAddressKey = GetAddressKey,
                ReadRawDataAsync = ReadRawDataAsync,
                DecodeValue = DecodeValue,
                TypeCode = 0x1884,
                TypeName = "CJ/T188-2004"
            }, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadRawData(Cjt188Address address)
        {
            using IDisposable operation = _channelLease!.Enter();
            byte[] request = Cjt188Frame.BuildReadRequest(address);
            NetworkStream stream = GetConnectedStream();
            stream.Write(request, 0, request.Length);

            byte[] response = ReadResponse(stream);
            return Cjt188Frame.ExtractReadData(response, address);
        }

        private async ValueTask<byte[]> ReadRawDataAsync(
            Cjt188Address address,
            CancellationToken cancellationToken)
        {
            using IDisposable operation = await _channelLease!.EnterAsync(cancellationToken).ConfigureAwait(false);
            byte[] request = Cjt188Frame.BuildReadRequest(address);
            NetworkStream stream = GetConnectedStream();
            await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);

            byte[] response = await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
            return Cjt188Frame.ExtractReadData(response, address);
        }

        private static object DecodeValue(Cjt188Address address, byte[] data, PlcDataType dataType)
        {
            return Cjt188DataCodec.Decode(data, address.DataIdentifier, dataType);
        }

        private static string GetAddressKey(Cjt188Address address)
        {
            return address.MeterType.ToString("X2") + ":" + address.MeterAddress + ":" + address.DataIdentifier;
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new NotSupportedException("CJ/T188-2004 内置驱动当前只支持读表。");
        }

        public ValueTask WriteAsync(
            string addressText,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("CJ/T188-2004 built-in driver is read-only.");
        }

        public void Dispose()
        {
            Disconnect();
        }

        private byte[] ReadResponse(NetworkStream stream)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds);
            while (DateTime.UtcNow <= deadline && stream.ReadByte() != 0x68)
            {
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("CJ/T188-2004 read timed out while waiting for frame start.");

            byte[] header = new byte[11];
            header[0] = 0x68;
            ReadExact(stream, header, 1, 10);
            byte[] frame = new byte[13 + header[10]];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            ReadExact(stream, frame, header.Length, frame.Length - header.Length);
            return frame;
        }

#if false
        private byte[] ReadResponseLegacy(NetworkStream stream)
        {
            MemoryStream buffer = new MemoryStream();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds);
            while (DateTime.UtcNow <= deadline)
            {
                int current = stream.ReadByte();
                if (current < 0)
                    continue;

                buffer.WriteByte((byte)current);
                byte[] bytes = buffer.ToArray();
                int start = FindStart(bytes);
                if (start < 0)
                    continue;
                if (bytes.Length < start + 13)
                    continue;

                int length = bytes[start + 10];
                int frameLength = 13 + length;
                if (bytes.Length < start + frameLength)
                    continue;

                byte[] frame = new byte[frameLength];
                Array.Copy(bytes, start, frame, 0, frameLength);
                return frame;
            }

            throw new TimeoutException("CJ/T188-2004读取超时。");
        }

#endif
        private async ValueTask<byte[]> ReadResponseAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            byte[] one = new byte[1];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds);
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("CJ/T188-2004 read timed out while waiting for frame start.");
                await ReadExactAsync(stream, one, 0, 1, cancellationToken).ConfigureAwait(false);
            }
            while (one[0] != 0x68);

            byte[] header = new byte[11];
            header[0] = 0x68;
            await ReadExactAsync(stream, header, 1, 10, cancellationToken).ConfigureAwait(false);
            byte[] frame = new byte[13 + header[10]];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            await ReadExactAsync(stream, frame, header.Length, frame.Length - header.Length, cancellationToken).ConfigureAwait(false);
            return frame;
        }

#if false
        private async ValueTask<byte[]> ReadResponseAsyncLegacy(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            MemoryStream buffer = new MemoryStream();
            byte[] one = new byte[1];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds);
            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    continue;

                buffer.WriteByte(one[0]);
                byte[] bytes = buffer.ToArray();
                int start = FindStart(bytes);
                if (start < 0)
                    continue;
                if (bytes.Length < start + 13)
                    continue;

                int length = bytes[start + 10];
                int frameLength = 13 + length;
                if (bytes.Length < start + frameLength)
                    continue;

                byte[] frame = new byte[frameLength];
                Array.Copy(bytes, start, frame, 0, frameLength);
                return frame;
            }

            throw new TimeoutException("CJ/T188-2004 read timed out.");
        }

#endif
        private static int FindStart(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0x68)
                    return i;
            }
            return -1;
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new IOException("CJ/T188-2004 transparent TCP connection was closed.");
                offset += read;
                count -= read;
            }
        }

        private static async ValueTask ReadExactAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                int read = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new IOException("CJ/T188-2004 transparent TCP connection was closed.");
                offset += read;
                count -= read;
            }
        }

        private NetworkStream GetConnectedStream()
        {
            NetworkStream? stream = _stream;
            if (!IsConnected || stream == null)
                throw new InvalidOperationException("CJ/T188-2004 client is not connected.");
            return stream;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("CJ/T188-2004客户端未连接。");
        }
    }
}
