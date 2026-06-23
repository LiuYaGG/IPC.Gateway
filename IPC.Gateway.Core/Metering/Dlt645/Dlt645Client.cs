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
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering.Dlt645
{
    
    
    
    
    
    
    
    
    
    public sealed class Dlt645Client : IPlcClient
    {
        private readonly PlcConnectionOptions _options;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;

        public Dlt645Client(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
        }

        public bool IsConnected
        {
            get { return _tcpClient != null && _tcpClient.Connected && _stream != null; }
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

            int port = _options.Port <= 0 ? 4001 : _options.Port;
            int timeout = _options.TimeoutMilliseconds <= 0 ? 3000 : _options.TimeoutMilliseconds;
            TcpClient client = new TcpClient();
            try
            {
                client.ReceiveTimeout = timeout;
                client.SendTimeout = timeout;
                client.Connect(_options.Host, port);

                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = timeout;
                stream.WriteTimeout = timeout;

                _tcpClient = client;
                _stream = stream;
            }
            catch
            {
                client.Close();
                _tcpClient = null;
                _stream = null;
                throw;
            }
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
        }

        public PlcReadResult Read(string addressText, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            Dlt645Address address = Dlt645Address.Parse(addressText);
            byte[] request = Dlt645Frame.BuildReadRequest(address);
            NetworkStream stream = GetConnectedStream();
            stream.Write(request, 0, request.Length);

            byte[] response = ReadResponse(stream);
            byte[] data = Dlt645Frame.ExtractReadData(response, address);
            object value = Dlt645DataCodec.Decode(data, address.DataIdentifier, dataType);
            return new PlcReadResult(0x6457, "DLT645-2007", value);
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new NotSupportedException("DLT645-2007 内置驱动当前只支持读表。");
        }

        public void Dispose()
        {
            Disconnect();
        }

        private byte[] ReadResponse(NetworkStream stream)
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

        private static int FindStart(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0x68)
                    return i;
            }
            return -1;
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
