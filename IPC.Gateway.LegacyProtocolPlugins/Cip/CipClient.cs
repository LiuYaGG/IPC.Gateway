/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Cip
* 项目描述 ：
* 类 名 称 ：CipClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Cip
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
using IPC.Plc.Communication.Core;







namespace IPC.Plc.Communication.Cip
{
    
    
    
    
    
    
    
    
    
    public sealed class CipClient : IPlcClient
    {
        private const ushort CommandRegisterSession = 0x0065;
        private const ushort CommandUnregisterSession = 0x0066;
        private const ushort CommandSendRRData = 0x006F;
        private const ushort CpfItemNullAddress = 0x0000;
        private const ushort CpfItemUnconnectedData = 0x00B2;
        private const int MaxDataBytesPerPacket = 400;

        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private uint _sessionHandle;
        private readonly int _slot;

        public CipClient(string host, int slot, int timeoutMilliseconds)
        {
            Host = host;
            Port = 44818;
            TimeoutMilliseconds = timeoutMilliseconds;
            _slot = slot;
        }

        public CipClient(PlcConnectionOptions options)
            : this(options.Host, options.Slot, options.TimeoutMilliseconds)
        {
            Port = options.Port;
        }

        public string Host { get; private set; }
        public int Port { get; set; }
        public int TimeoutMilliseconds { get; private set; }
        public bool IsConnected { get { return _tcpClient != null && _tcpClient.Connected && _sessionHandle != 0; } }
        public PlcProtocol Protocol { get { return PlcProtocol.RockwellCip; } }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            return ReadTag(address, dataType, elementCount, elementOffset);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            WriteTag(address, dataType, valueText, elementOffset);
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            _tcpClient = new TcpClient();
            IAsyncResult async = _tcpClient.BeginConnect(Host, Port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(TimeoutMilliseconds))
                throw new TimeoutException("连接 PLC 超时。");
            _tcpClient.EndConnect(async);
            _tcpClient.ReceiveTimeout = TimeoutMilliseconds;
            _tcpClient.SendTimeout = TimeoutMilliseconds;
            _stream = _tcpClient.GetStream();

            byte[] body = new byte[] { 0x01, 0x00, 0x00, 0x00 };
            EncapsulationPacket packet = SendEncapsulation(CommandRegisterSession, 0, body);
            _sessionHandle = packet.SessionHandle;
            if (_sessionHandle == 0)
                throw new InvalidOperationException("注册 EtherNet/IP 会话失败。");
        }

        public void Disconnect()
        {
            if (_stream != null && _sessionHandle != 0)
            {
                try
                {
                    SendEncapsulation(CommandUnregisterSession, _sessionHandle, new byte[0]);
                }
                catch
                {
                }
            }

            _sessionHandle = 0;
            if (_stream != null)
                _stream.Dispose();
            if (_tcpClient != null)
                _tcpClient.Close();
            _stream = null;
            _tcpClient = null;
        }

        public PlcReadResult ReadTag(string tagName, PlcDataType dataType, int elementCount)
        {
            return ReadTag(tagName, dataType, elementCount, 0);
        }

        public PlcReadResult ReadTag(string tagName, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            if (dataType == PlcDataType.BoolArray)
                return ReadPackedBoolArray(tagName, elementOffset, elementCount);

            if (dataType == PlcDataType.Bool && IsIndexedTag(tagName))
            {
                PlcReadResult result = ReadPackedBoolArray(tagName, elementOffset, 1);
                bool[] values = (bool[])result.Value;
                return new PlcReadResult(result.TypeCode, result.TypeName, values[0]);
            }

            if (PlcDataTypeHelper.IsArray(dataType) && (elementOffset > 0 || NeedsSegmentation(dataType, elementCount)))
                return ReadArraySegmented(tagName, dataType, elementOffset, elementCount);

            return ReadTagSingle(elementOffset > 0 ? BuildArrayElementTag(tagName, elementOffset) : tagName, dataType, elementCount);
        }

        private PlcReadResult ReadTagSingle(string tagName, PlcDataType dataType, int elementCount)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");

            byte[] path = CipPath.EncodeTagPath(tagName);
            MemoryStream request = new MemoryStream();
            request.WriteByte(0x4C);
            request.WriteByte((byte)(path.Length / 2));
            request.Write(path, 0, path.Length);
            int requestCount = dataType == PlcDataType.String ? 1 : elementCount;
            WriteUInt16(request, (ushort)requestCount);

            byte[] response = SendConnectedMessage(request.ToArray());
            int offset = ParseCipReply(response, 0xCC);
            if (response.Length < offset + 2)
                throw new InvalidOperationException("读取响应缺少数据类型。");

            ushort actualType = ReadUInt16(response, offset);
            byte[] data = Slice(response, offset + 2, response.Length - offset - 2);
            object value = CipDataCodec.Decode(dataType, actualType, data, elementCount);
            return new PlcReadResult(actualType, CipTypeCodes.ToName(actualType), value);
        }

        public void WriteTag(string tagName, PlcDataType dataType, string valueText)
        {
            WriteTag(tagName, dataType, valueText, 0);
        }

        public void WriteTag(string tagName, PlcDataType dataType, string valueText, int elementOffset)
        {
            int elementCount = CipDataCodec.GetElementCount(dataType, valueText, 1);
            byte[] data = CipDataCodec.Encode(dataType, valueText);
            WriteTag(tagName, dataType, data, elementCount, elementOffset);
        }

        public void WriteTag(string tagName, PlcDataType dataType, byte[] data, int elementCount)
        {
            WriteTag(tagName, dataType, data, elementCount, 0);
        }

        public void WriteTag(string tagName, PlcDataType dataType, byte[] data, int elementCount, int elementOffset)
        {
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            if (dataType == PlcDataType.Bool && IsIndexedTag(tagName))
            {
                WriteBoolArrayElements(tagName, data, 1, elementOffset);
                return;
            }

            if (dataType == PlcDataType.BoolArray)
            {
                WriteBoolArrayElements(tagName, data, elementCount, elementOffset);
                return;
            }

            if (PlcDataTypeHelper.IsArray(dataType) && (elementOffset > 0 || NeedsSegmentation(dataType, elementCount)))
            {
                WriteArraySegmented(tagName, dataType, data, elementCount, elementOffset);
                return;
            }

            WriteTagSingle(elementOffset > 0 ? BuildArrayElementTag(tagName, elementOffset) : tagName, dataType, data, elementCount);
        }

        private void WriteTagSingle(string tagName, PlcDataType dataType, byte[] data, int elementCount)
        {
            WriteTagSingle(tagName, CipTypeCodes.FromPlcDataType(dataType), data, elementCount);
        }

        private void WriteTagSingle(string tagName, ushort typeCode, byte[] data, int elementCount)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");

            byte[] path = CipPath.EncodeTagPath(tagName);
            MemoryStream request = new MemoryStream();
            request.WriteByte(0x4D);
            request.WriteByte((byte)(path.Length / 2));
            request.Write(path, 0, path.Length);
            WriteUInt16(request, typeCode);
            WriteUInt16(request, (ushort)elementCount);
            request.Write(data, 0, data.Length);

            byte[] response = SendConnectedMessage(request.ToArray());
            ParseCipReply(response, 0xCD);
        }

        private PlcReadResult ReadArraySegmented(string tagName, PlcDataType dataType, int elementOffset, int elementCount)
        {
            Array values = PlcDataTypeHelper.CreateArray(dataType, elementCount);
            int copied = 0;
            ushort actualType = 0;
            int maxElements = GetMaxElementsPerPacket(dataType);

            while (copied < elementCount)
            {
                int chunkCount = Math.Min(maxElements, elementCount - copied);
                string chunkTag = BuildArrayElementTag(tagName, elementOffset + copied);
                PlcReadResult chunk = ReadTagSingle(chunkTag, dataType, chunkCount);
                Array chunkValues = (Array)chunk.Value;
                Array.Copy(chunkValues, 0, values, copied, chunkCount);
                actualType = chunk.TypeCode;
                copied += chunkCount;
            }

            return new PlcReadResult(actualType, CipTypeCodes.ToName(actualType), values);
        }

        private void WriteArraySegmented(string tagName, PlcDataType dataType, byte[] data, int elementCount, int elementOffset)
        {
            int elementSize = PlcDataTypeHelper.GetElementSize(dataType);
            if (data == null || data.Length < elementCount * elementSize)
                throw new ArgumentException("写入数据长度不足。", "data");

            int written = 0;
            int maxElements = GetMaxElementsPerPacket(dataType);
            while (written < elementCount)
            {
                int chunkCount = Math.Min(maxElements, elementCount - written);
                int chunkBytes = chunkCount * elementSize;
                byte[] chunkData = Slice(data, written * elementSize, chunkBytes);
                string chunkTag = BuildArrayElementTag(tagName, elementOffset + written);
                WriteTagSingle(chunkTag, dataType, chunkData, chunkCount);
                written += chunkCount;
            }
        }

        private void WriteBoolArrayElements(string tagName, byte[] data, int elementCount, int elementOffset)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");
            if (data == null || data.Length < elementCount)
                throw new ArgumentException("写入数据长度不足。", "data");

            string baseTag;
            int bitOffset;
            NormalizeBoolArrayTag(tagName, elementOffset, out baseTag, out bitOffset);

            int firstWord = bitOffset / 32;
            int firstBit = bitOffset % 32;
            int wordCount = (firstBit + elementCount + 31) / 32;

            PlcReadResult current = ReadTagSingle(BuildArrayElementTagAlways(baseTag, firstWord), PlcDataType.Int32Array, wordCount);
            int[] words = new int[wordCount];
            Array.Copy((int[])current.Value, 0, words, 0, wordCount);

            for (int i = 0; i < elementCount; i++)
            {
                int absoluteBit = firstBit + i;
                int wordIndex = absoluteBit / 32;
                int bitIndex = absoluteBit % 32;
                int mask = 1 << bitIndex;
                if (data[i] == 0)
                    words[wordIndex] &= ~mask;
                else
                    words[wordIndex] |= mask;
            }

            byte[] packed = new byte[wordCount * 4];
            for (int i = 0; i < wordCount; i++)
            {
                byte[] wordBytes = BitConverter.GetBytes(words[i]);
                Buffer.BlockCopy(wordBytes, 0, packed, i * 4, 4);
            }

            WriteTagSingle(BuildArrayElementTagAlways(baseTag, firstWord), CipTypeCodes.Dword, packed, wordCount);
        }

        private PlcReadResult ReadPackedBoolArray(string tagName, int elementOffset, int elementCount)
        {
            string baseTag;
            int bitOffset;
            NormalizeBoolArrayTag(tagName, elementOffset, out baseTag, out bitOffset);

            int firstWord = bitOffset / 32;
            int firstBit = bitOffset % 32;
            int wordCount = (firstBit + elementCount + 31) / 32;
            PlcReadResult current = ReadTagSingle(BuildArrayElementTagAlways(baseTag, firstWord), PlcDataType.Int32Array, wordCount);
            int[] words = (int[])current.Value;
            bool[] values = new bool[elementCount];

            for (int i = 0; i < elementCount; i++)
            {
                int absoluteBit = firstBit + i;
                int wordIndex = absoluteBit / 32;
                int bitIndex = absoluteBit % 32;
                values[i] = ((words[wordIndex] >> bitIndex) & 1) != 0;
            }

            return new PlcReadResult(current.TypeCode, current.TypeName, values);
        }

        private static void NormalizeBoolArrayTag(string tagName, int elementOffset, out string baseTag, out int bitOffset)
        {
            int existingIndex;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                bitOffset = existingIndex + elementOffset;
            else
            {
                baseTag = tagName;
                bitOffset = elementOffset;
            }
        }

        private static bool IsIndexedTag(string tagName)
        {
            string baseTag;
            int index;
            return TrySplitTrailingSingleIndex(tagName, out baseTag, out index);
        }

        private static bool NeedsSegmentation(PlcDataType dataType, int elementCount)
        {
            return PlcDataTypeHelper.IsArray(dataType) && elementCount > GetMaxElementsPerPacket(dataType);
        }

        private static int GetMaxElementsPerPacket(PlcDataType dataType)
        {
            int elementSize = PlcDataTypeHelper.GetElementSize(dataType);
            return Math.Max(1, MaxDataBytesPerPacket / elementSize);
        }

        private static string BuildArrayElementTag(string tagName, int elementOffset)
        {
            if (elementOffset == 0)
                return tagName;

            int existingIndex;
            string baseTag;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                return baseTag + "[" + (existingIndex + elementOffset).ToString() + "]";

            return tagName + "[" + elementOffset.ToString() + "]";
        }

        private static string BuildArrayElementTagAlways(string tagName, int elementOffset)
        {
            int existingIndex;
            string baseTag;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                return baseTag + "[" + (existingIndex + elementOffset).ToString() + "]";

            return tagName + "[" + elementOffset.ToString() + "]";
        }

        private static bool TrySplitTrailingSingleIndex(string tagName, out string baseTag, out int index)
        {
            baseTag = tagName;
            index = 0;
            if (string.IsNullOrEmpty(tagName) || !tagName.EndsWith("]", StringComparison.Ordinal))
                return false;

            int open = tagName.LastIndexOf('[');
            if (open < 0 || open == tagName.Length - 2)
                return false;

            string indexText = tagName.Substring(open + 1, tagName.Length - open - 2);
            if (indexText.IndexOf(',') >= 0 || !int.TryParse(indexText, out index) || index < 0)
                return false;

            baseTag = tagName.Substring(0, open);
            return true;
        }

        public void Dispose()
        {
            Disconnect();
        }

        private byte[] SendConnectedMessage(byte[] cipRequest)
        {
            if (!IsConnected)
                Connect();

            byte[] unconnectedSend = BuildUnconnectedSend(cipRequest);
            MemoryStream body = new MemoryStream();
            WriteUInt32(body, 0);
            WriteUInt16(body, 0);
            WriteUInt16(body, 2);
            WriteUInt16(body, CpfItemNullAddress);
            WriteUInt16(body, 0);
            WriteUInt16(body, CpfItemUnconnectedData);
            WriteUInt16(body, (ushort)unconnectedSend.Length);
            body.Write(unconnectedSend, 0, unconnectedSend.Length);

            EncapsulationPacket packet = SendEncapsulation(CommandSendRRData, _sessionHandle, body.ToArray());
            byte[] itemData = ExtractUnconnectedData(packet.Body);
            return ExtractEmbeddedCipResponse(itemData);
        }

        private byte[] BuildUnconnectedSend(byte[] cipRequest)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte(0x52);
            stream.WriteByte(0x02);
            stream.WriteByte(0x20);
            stream.WriteByte(0x06);
            stream.WriteByte(0x24);
            stream.WriteByte(0x01);
            stream.WriteByte(0x0A);
            stream.WriteByte(0x0E);
            WriteUInt16(stream, (ushort)cipRequest.Length);
            stream.Write(cipRequest, 0, cipRequest.Length);
            if ((cipRequest.Length % 2) != 0)
                stream.WriteByte(0);
            stream.WriteByte(0x01);
            stream.WriteByte(0x00);
            stream.WriteByte(0x01);
            stream.WriteByte((byte)_slot);
            return stream.ToArray();
        }

        private EncapsulationPacket SendEncapsulation(ushort command, uint sessionHandle, byte[] body)
        {
            byte[] header = new byte[24];
            PutUInt16(header, 0, command);
            PutUInt16(header, 2, (ushort)body.Length);
            PutUInt32(header, 4, sessionHandle);
            PutUInt32(header, 8, 0);
            PutUInt32(header, 20, 0);

            _stream.Write(header, 0, header.Length);
            if (body.Length > 0)
                _stream.Write(body, 0, body.Length);

            byte[] responseHeader = ReadExact(24);
            ushort responseCommand = ReadUInt16(responseHeader, 0);
            ushort length = ReadUInt16(responseHeader, 2);
            uint responseSession = ReadUInt32(responseHeader, 4);
            uint status = ReadUInt32(responseHeader, 8);
            byte[] responseBody = length == 0 ? new byte[0] : ReadExact(length);

            if (responseCommand != command)
                throw new InvalidOperationException("EtherNet/IP 响应命令不匹配。");
            if (status != 0)
                throw new InvalidOperationException("EtherNet/IP 封装错误: 0x" + status.ToString("X8"));

            return new EncapsulationPacket(responseSession, responseBody);
        }

        private byte[] ExtractUnconnectedData(byte[] body)
        {
            if (body.Length < 8)
                throw new InvalidOperationException("SendRRData 响应过短。");

            ushort itemCount = ReadUInt16(body, 6);
            int offset = 8;
            for (int i = 0; i < itemCount; i++)
            {
                if (body.Length < offset + 4)
                    throw new InvalidOperationException("CPF 项响应过短。");
                ushort typeId = ReadUInt16(body, offset);
                ushort length = ReadUInt16(body, offset + 2);
                offset += 4;
                if (body.Length < offset + length)
                    throw new InvalidOperationException("CPF 项长度无效。");
                if (typeId == CpfItemUnconnectedData)
                    return Slice(body, offset, length);
                offset += length;
            }

            throw new InvalidOperationException("SendRRData 响应没有 Unconnected Data 项。");
        }

        private byte[] ExtractEmbeddedCipResponse(byte[] data)
        {
            if (data.Length >= 4 && data[0] == 0xD2)
            {
                int offset = ParseCipReply(data, 0xD2);
                if (offset >= data.Length)
                    throw new InvalidOperationException("Unconnected Send 响应没有内层 CIP 数据。");
                return Slice(data, offset, data.Length - offset);
            }
            return data;
        }

        private static int ParseCipReply(byte[] response, byte expectedService)
        {
            if (response.Length < 4)
                throw new InvalidOperationException("CIP 响应过短。");

            if (response[0] != expectedService)
                throw new InvalidOperationException("CIP 响应服务码不匹配，期望 0x" + expectedService.ToString("X2") + "，实际 0x" + response[0].ToString("X2"));

            byte generalStatus = response[2];
            byte additionalWords = response[3];
            int offset = 4 + additionalWords * 2;
            if (offset > response.Length)
                throw new InvalidOperationException("CIP 附加状态长度无效。");

            if (generalStatus != 0)
                throw new InvalidOperationException("CIP 错误: general status 0x" + generalStatus.ToString("X2") + ", additional: " + FormatAdditionalStatus(response, 4, additionalWords));

            return offset;
        }

        private static string FormatAdditionalStatus(byte[] response, int offset, int words)
        {
            if (words <= 0)
                return "none";

            string[] values = new string[words];
            for (int i = 0; i < words; i++)
                values[i] = "0x" + ReadUInt16(response, offset + i * 2).ToString("X4");
            return string.Join(", ", values);
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

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
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

        private static void PutUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void PutUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        
        
        
        
        
        
        
        
        
        private sealed class EncapsulationPacket
        {
            public EncapsulationPacket(uint sessionHandle, byte[] body)
            {
                SessionHandle = sessionHandle;
                Body = body;
            }

            public uint SessionHandle { get; private set; }
            public byte[] Body { get; private set; }
        }
    }
}
