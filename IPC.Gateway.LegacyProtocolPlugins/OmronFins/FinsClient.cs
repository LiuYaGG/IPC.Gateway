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
using System.IO;
using System.Net.Sockets;
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    
    
    
    
    
    
    
    
    
    public sealed class FinsClient : IPlcClient
    {
        private const int MaxWordCount = 240;
        private const int MaxBitCount = 480;

        private readonly PlcConnectionOptions _options;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private byte _clientNode;
        private byte _serverNode;
        private byte _sid;

        public FinsClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            _options = options;
        }

        public bool IsConnected
        {
            get { return _tcpClient != null && _tcpClient.Connected; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.OmronFins; }
        }

        public void Connect()
        {
            Disconnect();

            int port = _options.Port <= 0 ? 9600 : _options.Port;
            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = _options.TimeoutMilliseconds;
            _tcpClient.SendTimeout = _options.TimeoutMilliseconds;
            _tcpClient.Connect(_options.Host, port);
            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = _options.TimeoutMilliseconds;
            _stream.WriteTimeout = _options.TimeoutMilliseconds;

            Handshake();
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
            EnsureSupportedType(dataType);
            if (elementCount <= 0)
                elementCount = 1;

            FinsAddress address = FinsAddress.Parse(addressText, dataType);
            if (FinsDataCodec.IsBitType(dataType))
            {
                int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                byte[] bitBytes = ReadMemory(start.Area.BitCode, start.WordAddress, start.BitIndex, count, MaxBitCount);
                object value = FinsDataCodec.DecodeBits(dataType, bitBytes, count);
                return new PlcReadResult(start.Area.BitCode, start.Area.Name + ".BIT", value);
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("非 BOOL 类型不能使用 FINS 位地址。");

            bool usesCount = PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String;
            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            int words = FinsDataCodec.GetWordCount(dataType, usesCount ? elementCount : 1);
            byte[] data = ReadMemory(wordStart.Area.WordCode, wordStart.WordAddress, 0, words, MaxWordCount);
            object result = FinsDataCodec.DecodeWords(dataType, data, usesCount ? elementCount : 1, _options.WordOrder);
            return new PlcReadResult(wordStart.Area.WordCode, wordStart.Area.Name + ".WORD", result);
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            EnsureSupportedType(dataType);

            FinsAddress address = FinsAddress.Parse(addressText, dataType);
            if (FinsDataCodec.IsBitType(dataType))
            {
                byte[] values = FinsDataCodec.EncodeBits(dataType, valueText);
                FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                WriteMemory(start.Area.BitCode, start.WordAddress, start.BitIndex, values, values.Length, MaxBitCount);
                return;
            }

            if (address.HasBitIndex)
                throw new NotSupportedException("非 BOOL 类型不能使用 FINS 位地址。");

            int wordOffset = PlcDataTypeHelper.IsArray(dataType) ? FinsDataCodec.GetWordOffset(dataType, elementOffset) : 0;
            FinsAddress wordStart = address.OffsetWords(wordOffset);
            byte[] data = FinsDataCodec.EncodeWords(dataType, valueText, _options.WordOrder);
            WriteMemory(wordStart.Area.WordCode, wordStart.WordAddress, 0, data, data.Length / 2, MaxWordCount);
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
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, copied, areaCode == 0x30 || areaCode == 0x31 || areaCode == 0x32 || areaCode == 0x33 || areaCode == 0x02 || areaCode == 0x20);
                byte[] command = BuildMemoryCommand(0x01, 0x01, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, null);
                byte[] response = SendFinsCommand(command);
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

        private void WriteMemory(byte areaCode, int wordAddress, int bitIndex, byte[] data, int count, int segmentLimit)
        {
            int written = 0;
            int bytesPerElement = IsBitAreaCode(areaCode) ? 1 : 2;
            while (written < count)
            {
                int segmentCount = Math.Min(segmentLimit, count - written);
                AddressParts parts = OffsetAddress(wordAddress, bitIndex, written, IsBitAreaCode(areaCode));
                byte[] segmentData = new byte[segmentCount * bytesPerElement];
                Buffer.BlockCopy(data, written * bytesPerElement, segmentData, 0, segmentData.Length);
                byte[] command = BuildMemoryCommand(0x01, 0x02, areaCode, parts.WordAddress, parts.BitIndex, segmentCount, segmentData);
                byte[] response = SendFinsCommand(command);
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
            WriteUInt32(request, 16, 0);
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

            byte[] payload = ReadExact((int)length - 8);
            _clientNode = (byte)ReadUInt32(payload, 0);
            _serverNode = (byte)ReadUInt32(payload, 4);
            if (_clientNode == 0)
                _clientNode = 0xEF;
        }

        private byte[] SendFinsCommand(byte[] command)
        {
            byte[] finsFrame = BuildFinsFrame(command);
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
            if (length < 8)
                throw new InvalidOperationException("FINS/TCP 响应长度不足。");
            return ReadExact((int)length - 8);
        }

        private byte[] BuildFinsFrame(byte[] command)
        {
            byte[] frame = new byte[10 + command.Length];
            frame[0] = 0x80;
            frame[1] = 0x00;
            frame[2] = 0x02;
            frame[3] = GetDestinationNetwork();
            frame[4] = _serverNode;
            frame[5] = GetDestinationUnit();
            frame[6] = 0x00;
            frame[7] = _clientNode;
            frame[8] = 0x00;
            frame[9] = NextSid();
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
                throw new InvalidOperationException("FINS错误: end code 0x" + endCode.ToString("X4") + " (" + GetEndCodeName(endCode) + ")");
        }

        private static AddressParts OffsetAddress(int wordAddress, int bitIndex, int offset, bool isBitArea)
        {
            if (!isBitArea)
                return new AddressParts(wordAddress + offset, bitIndex);

            int absoluteBit = bitIndex + offset;
            return new AddressParts(wordAddress + absoluteBit / 16, absoluteBit % 16);
        }

        private void EnsureConnected()
        {
            if (!IsConnected || _stream == null)
                Connect();
        }

        private static void EnsureSupportedType(PlcDataType dataType)
        {
            if (dataType == PlcDataType.Coil ||
                dataType == PlcDataType.CoilArray ||
                dataType == PlcDataType.DiscreteInput ||
                dataType == PlcDataType.DiscreteInputArray)
                throw new NotSupportedException("FINS 协议不使用 Modbus Coil/Discrete Input 类型，请选择 BOOL 或 BOOL[]。");
        }

        private byte GetDestinationNetwork()
        {
            if (_options.Rack < 0)
                return 0;
            if (_options.Rack > 127)
                return 127;
            return (byte)_options.Rack;
        }

        private byte GetDestinationUnit()
        {
            if (_options.Slot < 0)
                return 0;
            if (_options.Slot > 31)
                return 31;
            return (byte)_options.Slot;
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
                   areaCode == 0x20;
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
