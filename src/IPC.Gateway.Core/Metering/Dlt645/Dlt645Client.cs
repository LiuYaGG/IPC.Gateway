/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Dlt645
* 项目描述 ：
* 类 名 称 ：Dlt645Client
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Dlt645
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
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Metering;

namespace IPC.Plc.Communication.Metering.Dlt645
{
    
    
    
    
    
    
    
    
    
    public sealed class Dlt645Client : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private SharedTransparentTcpLease? _channelLease;
        private NetworkStream? _stream;

        public Dlt645Client(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
        }

        public bool IsConnected
        {
            get { return _channelLease?.IsConnected == true && _stream != null; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.Dlt6452007; }
        }

        public void Connect()
        {
            Disconnect();
            if (string.IsNullOrWhiteSpace(_options.Host))
                throw new InvalidOperationException("DLT645-2007 当前内置驱动使用 TCP 透明传输，请配置串口服务器 IP。");

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
                throw new InvalidOperationException("DLT645-2007 TCP host is not configured.");

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
            Dlt645Address address = Dlt645Address.Parse(addressText);
            byte[] data = ReadRawData(address);
            object value = DecodeValue(address, data, dataType);
            return new PlcReadResult(0x6457, "DLT645-2007", value);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string addressText,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            Dlt645Address address = Dlt645Address.Parse(addressText);
            byte[] data = await ReadRawDataAsync(address, cancellationToken).ConfigureAwait(false);
            object value = DecodeValue(address, data, dataType);
            return new PlcReadResult(0x6457, "DLT645-2007", value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            EnsureConnected();
            return MeterBatchReadExecutor.ReadMany(requests, new MeterBatchReadContext<Dlt645Address>
            {
                ParseAddress = Dlt645Address.Parse,
                GetAddressKey = GetAddressKey,
                ReadRawData = ReadRawData,
                DecodeValue = DecodeValue,
                TypeCode = 0x6457,
                TypeName = "DLT645-2007"
            });
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            return await MeterBatchReadExecutor.ReadManyAsync(requests, new MeterAsyncBatchReadContext<Dlt645Address>
            {
                ParseAddress = Dlt645Address.Parse,
                GetAddressKey = GetAddressKey,
                ReadRawDataAsync = ReadRawDataAsync,
                DecodeValue = DecodeValue,
                TypeCode = 0x6457,
                TypeName = "DLT645-2007"
            }, cancellationToken).ConfigureAwait(false);
        }

        private byte[] ReadRawData(Dlt645Address address)
        {
            using IDisposable operation = _channelLease!.Enter();
            byte[] request = Dlt645Frame.BuildReadRequest(address);
            NetworkStream stream = GetConnectedStream();
            stream.Write(request, 0, request.Length);

            byte[] response = ReadResponse(stream);
            return Dlt645Frame.ExtractReadData(response, address);
        }

        private async ValueTask<byte[]> ReadRawDataAsync(
            Dlt645Address address,
            CancellationToken cancellationToken)
        {
            using IDisposable operation = await _channelLease!.EnterAsync(cancellationToken).ConfigureAwait(false);
            byte[] request = Dlt645Frame.BuildReadRequest(address);
            NetworkStream stream = GetConnectedStream();
            await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);

            byte[] response = await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
            return Dlt645Frame.ExtractReadData(response, address);
        }

        private static object DecodeValue(Dlt645Address address, byte[] data, PlcDataType dataType)
        {
            return Dlt645DataCodec.Decode(data, address.DataIdentifier, dataType);
        }

        private static string GetAddressKey(Dlt645Address address)
        {
            return address.MeterAddress.ToUpperInvariant() + ":" + address.DataIdentifier;
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new NotSupportedException("DLT645-2007 内置驱动当前只支持读表。");
        }

        public ValueTask WriteAsync(
            string addressText,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("DLT645-2007 built-in driver is read-only.");
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
                throw new TimeoutException("DLT645-2007 read timed out while waiting for frame start.");

            byte[] header = new byte[10];
            header[0] = 0x68;
            ReadExact(stream, header, 1, 9);
            byte[] frame = new byte[12 + header[9]];
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
                if (bytes.Length < start + 12)
                    continue;

                int length = bytes[start + 9];
                int frameLength = 12 + length;
                if (bytes.Length < start + frameLength)
                    continue;

                byte[] frame = new byte[frameLength];
                Array.Copy(bytes, start, frame, 0, frameLength);
                return frame;
            }

            throw new TimeoutException("DLT645-2007读取超时。");
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
                    throw new TimeoutException("DLT645-2007 read timed out while waiting for frame start.");
                await ReadExactAsync(stream, one, 0, 1, cancellationToken).ConfigureAwait(false);
            }
            while (one[0] != 0x68);

            byte[] header = new byte[10];
            header[0] = 0x68;
            await ReadExactAsync(stream, header, 1, 9, cancellationToken).ConfigureAwait(false);
            byte[] frame = new byte[12 + header[9]];
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
                if (bytes.Length < start + 12)
                    continue;

                int length = bytes[start + 9];
                int frameLength = 12 + length;
                if (bytes.Length < start + frameLength)
                    continue;

                byte[] frame = new byte[frameLength];
                Array.Copy(bytes, start, frame, 0, frameLength);
                return frame;
            }

            throw new TimeoutException("DLT645-2007 read timed out.");
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
                    throw new IOException("DLT645-2007 transparent TCP connection was closed.");
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
                    throw new IOException("DLT645-2007 transparent TCP connection was closed.");
                offset += read;
                count -= read;
            }
        }

        private NetworkStream GetConnectedStream()
        {
            NetworkStream? stream = _stream;
            if (!IsConnected || stream == null)
                throw new InvalidOperationException("DLT645-2007 client is not connected.");
            return stream;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("DLT645-2007客户端未连接。");
        }
    }
}
