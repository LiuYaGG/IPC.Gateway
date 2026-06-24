/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiMc1E
* 项目描述 ：
* 类 名 称 ：Mc1EClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiMc1E
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Plc.Communication.MitsubishiMc1E
{
    
    
    
    
    
    
    
    
    
    public sealed class Mc1EClient : IPlcClient
    {
        private const int MaxWordPoints = 64;
        private const int MaxBitPoints = 160;

        private readonly PlcConnectionOptions _options;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private UdpClient _udpClient;
        private IPEndPoint _udpRemoteEndPoint;

        public Mc1EClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            _options = options;
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

        public PlcProtocol Protocol { get { return PlcProtocol.MitsubishiMc1E; } }

        public void Connect()
        {
            if (IsConnected)
                return;

            if (_options.Transport == NetworkTransport.Udp)
            {
                IPAddress[] addresses = Dns.GetHostAddresses(_options.Host);
                if (addresses == null || addresses.Length == 0)
                    throw new InvalidOperationException("PLC��ַ��֤ʧ��: " + _options.Host);

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
                throw new TimeoutException("���� Mitsubishi MC 1E PLC ��ʱ");

            _tcpClient.EndConnect(async);
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

        private byte[] ReadWords(Mc1EAddress address, int points)
        {
            return Send(BuildRequest(0x01, address, points, null), 0x81);
        }

        private void WriteWords(Mc1EAddress address, byte[] data, int points)
        {
            Send(BuildRequest(0x03, address, points, data), 0x83);
        }

        private byte[] ReadBits(Mc1EAddress address, int points)
        {
            return Send(BuildRequest(0x00, address, points, null), 0x80);
        }

        private void WriteBits(Mc1EAddress address, byte[] packedBits, int points)
        {
            Send(BuildRequest(0x02, address, points, packedBits), 0x82);
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
            if (!IsConnected)
                Connect();

            byte[] response;
            if (_options.Transport == NetworkTransport.Udp)
            {
                _udpClient.Send(request, request.Length);
                IPEndPoint remote = null;
                response = _udpClient.Receive(ref remote);
            }
            else
            {
                _stream.Write(request, 0, request.Length);
                byte[] header = ReadExact(2);
                if (header[0] != expectedResponse)
                    throw new InvalidOperationException("MC 1E ��Ӧ��ʱ������ 0x" + header[0].ToString("X2"));
                if (header[1] == 0)
                    return ReadRemainingData(expectedResponse, request);
                if (header[1] == 0x5B)
                {
                    byte[] detail = ReadExact(2);
                    throw new InvalidOperationException("MC 1E ȷ�ϴ���: 0x" + detail[0].ToString("X2") + detail[1].ToString("X2"));
                }
                throw new InvalidOperationException("MC 1E �������� 0x" + header[1].ToString("X2"));
            }

            if (response == null || response.Length < 2)
                throw new InvalidOperationException("MC 1E ��Ӧ������Ч");
            if (response[0] != expectedResponse)
                throw new InvalidOperationException("MC 1E ��Ӧ��ʱ������ 0x" + response[0].ToString("X2"));
            if (response[1] == 0)
            {
                byte[] data = new byte[response.Length - 2];
                Buffer.BlockCopy(response, 2, data, 0, data.Length);
                return data;
            }
            if (response[1] == 0x5B)
            {
                if (response.Length < 4)
                    throw new InvalidOperationException("MC 1E ������Ӧ������Ч");
                throw new InvalidOperationException("MC 1E ȷ�ϴ��� 0x" + response[2].ToString("X2") + response[3].ToString("X2"));
            }

            throw new InvalidOperationException("MC 1E �������� 0x" + response[1].ToString("X2"));
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

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("�����ѹر�");
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
