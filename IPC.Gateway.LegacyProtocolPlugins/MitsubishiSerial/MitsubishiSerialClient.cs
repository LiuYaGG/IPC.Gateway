/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiSerial
* 项目描述 ：
* 类 名 称 ：MitsubishiSerialClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiSerial
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
using System.IO.Ports;
using System.Text;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Plc.Communication.MitsubishiSerial
{
    
    
    
    
    
    
    
    
    
    public sealed class MitsubishiSerialClient : IPlcClient
    {
        private const byte Stx = 0x02;
        private const byte Etx = 0x03;
        private const byte Ack = 0x06;
        private const byte Nak = 0x15;
        private const int MaxWordPoints = 64;
        private const int MaxBitPoints = 256;

        private readonly PlcConnectionOptions _options;
        private SerialPort _serialPort;

        public MitsubishiSerialClient(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            _options = options;
        }

        public bool IsConnected
        {
            get { return _serialPort != null && _serialPort.IsOpen; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.MitsubishiSerial; }
        }

        public void Connect()
        {
            Disconnect();

            string portName = string.IsNullOrWhiteSpace(_options.Host) ? "COM1" : _options.Host.Trim();
            int baudRate = _options.Port <= 0 ? 9600 : _options.Port;
            int dataBits = _options.DataBits <= 0 ? 7 : _options.DataBits;
            _serialPort = new SerialPort(
                portName,
                baudRate,
                IPC.Gateway.LegacyProtocolPlugins.SerialPortOptionMapper.MapParity(_options.SerialParity),
                dataBits,
                IPC.Gateway.LegacyProtocolPlugins.SerialPortOptionMapper.MapStopBits(_options.SerialStopBits));
            _serialPort.ReadTimeout = _options.TimeoutMilliseconds;
            _serialPort.WriteTimeout = _options.TimeoutMilliseconds;
            _serialPort.Open();
        }

        public void Disconnect()
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException("elementCount");
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            MitsubishiSerialAddress serialAddress = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.Bool)
                return ReadBool(serialAddress);
            if (dataType == PlcDataType.BoolArray)
                return ReadBoolArray(serialAddress, elementCount);

            int wordCount = McDataCodec.GetWordCount(dataType, elementCount);
            byte[] data = ReadWordsSegmented(serialAddress, wordCount);
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
            EnsureConnected();
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException("elementOffset");

            int writeElementCount = McDataCodec.GetElementCount(dataType, valueText, elementCount);
            MitsubishiSerialAddress serialAddress = BuildAddress(address, dataType, elementOffset);
            byte[] data = McDataCodec.Encode(dataType, valueText, writeElementCount);

            if (dataType == PlcDataType.Bool)
            {
                WriteBool(serialAddress, data[0] != 0);
                return;
            }

            if (dataType == PlcDataType.BoolArray)
            {
                WriteBoolArray(serialAddress, data, writeElementCount);
                return;
            }

            int wordCount = GetWordCountForWrite(data);
            PreserveOddStringTailByte(dataType, writeElementCount, serialAddress, data, wordCount);
            WriteWordsSegmented(serialAddress, data, wordCount);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private MitsubishiSerialAddress BuildAddress(string address, PlcDataType dataType, int elementOffset)
        {
            MitsubishiSerialAddress parsed = MitsubishiSerialAddress.Parse(address);
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray)
                return parsed.AddBitOffset(elementOffset);
            return parsed.AddDeviceOffset(McDataCodec.GetDeviceOffset(dataType, elementOffset));
        }

        private PlcReadResult ReadBool(MitsubishiSerialAddress address)
        {
            bool value;
            if (address.IsBitDevice)
            {
                bool[] values = ReadBits(address, 1);
                value = values[0];
            }
            else
            {
                byte[] word = ReadWordsSegmented(address, 1);
                value = (BitConverter.ToUInt16(word, 0) & (1 << address.BitOffset)) != 0;
            }
            return new PlcReadResult(0, "BOOL", value);
        }

        private PlcReadResult ReadBoolArray(MitsubishiSerialAddress address, int count)
        {
            bool[] values;
            if (address.IsBitDevice)
            {
                values = ReadBitsSegmented(address, count);
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

        private void WriteBool(MitsubishiSerialAddress address, bool value)
        {
            if (address.IsBitDevice)
            {
                WriteBits(address, new[] { value }, 1);
                return;
            }

            byte[] word = ReadWordsSegmented(address, 1);
            McDataCodec.SetWordBit(word, address.BitOffset, value);
            WriteWordsSegmented(address, word, 1);
        }

        private void WriteBoolArray(MitsubishiSerialAddress address, byte[] values, int count)
        {
            if (address.IsBitDevice)
            {
                bool[] boolValues = new bool[count];
                for (int i = 0; i < count; i++)
                    boolValues[i] = values[i] != 0;
                WriteBitsSegmented(address, boolValues, count);
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

        private byte[] ReadWordsSegmented(MitsubishiSerialAddress address, int wordCount)
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

        private void WriteWordsSegmented(MitsubishiSerialAddress address, byte[] data, int wordCount)
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

        private bool[] ReadBitsSegmented(MitsubishiSerialAddress address, int count)
        {
            bool[] result = new bool[count];
            int offset = 0;
            while (offset < count)
            {
                int chunk = Math.Min(MaxBitPoints, count - offset);
                bool[] values = ReadBits(address.AddDeviceOffset(offset), chunk);
                Array.Copy(values, 0, result, offset, chunk);
                offset += chunk;
            }
            return result;
        }

        private void WriteBitsSegmented(MitsubishiSerialAddress address, bool[] values, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int chunk = Math.Min(MaxBitPoints, count - offset);
                bool[] chunkValues = new bool[chunk];
                Array.Copy(values, offset, chunkValues, 0, chunk);
                WriteBits(address.AddDeviceOffset(offset), chunkValues, chunk);
                offset += chunk;
            }
        }

        private byte[] ReadWords(MitsubishiSerialAddress address, int points)
        {
            string response = SendAscii("RR", address, points, null);
            return DecodeWordData(response, points);
        }

        private void WriteWords(MitsubishiSerialAddress address, byte[] data, int points)
        {
            SendAscii("WR", address, points, EncodeWordData(data, points));
        }

        private bool[] ReadBits(MitsubishiSerialAddress address, int points)
        {
            string response = SendAscii("RS", address, points, null);
            return DecodeBitData(response, points);
        }

        private void WriteBits(MitsubishiSerialAddress address, bool[] values, int points)
        {
            SendAscii("WS", address, points, EncodeBitData(values, points));
        }

        private string SendAscii(string command, MitsubishiSerialAddress address, int points, string data)
        {
            string body = command + address.DeviceCode + address.DeviceNumber.ToString("X4", CultureInfo.InvariantCulture) + points.ToString("X2", CultureInfo.InvariantCulture) + (data ?? string.Empty);
            byte[] frame = BuildFrame(body);
            _serialPort.DiscardInBuffer();
            _serialPort.Write(frame, 0, frame.Length);

            int first = _serialPort.ReadByte();
            if (first == Ack)
                return string.Empty;
            if (first == Nak)
                throw new InvalidOperationException("Mitsubishi ϵ�� PLC ���� NAK");
            if (first != Stx)
                throw new InvalidOperationException("Mitsubishi ϵ�� ��Ӧ��ʱ������ 0x" + first.ToString("X2", CultureInfo.InvariantCulture));

            MemoryStream content = new MemoryStream();
            while (true)
            {
                int value = _serialPort.ReadByte();
                if (value < 0)
                    throw new IOException("Mitsubishi ϵ�� �����ѶϿ�");
                if (value == Etx)
                    break;
                content.WriteByte((byte)value);
            }

            byte[] bccBytes = ReadExact(2);
            byte expected = ComputeBcc(content.ToArray(), Etx);
            byte actual = byte.Parse(Encoding.ASCII.GetString(bccBytes), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (expected != actual)
                throw new InvalidOperationException("Mitsubishi ϵ�� BCC У��ʧ��");

            return Encoding.ASCII.GetString(content.ToArray());
        }

        private static byte[] BuildFrame(string body)
        {
            byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
            byte bcc = ComputeBcc(bodyBytes, Etx);
            string bccText = bcc.ToString("X2", CultureInfo.InvariantCulture);
            byte[] frame = new byte[1 + bodyBytes.Length + 1 + 2];
            frame[0] = Stx;
            Buffer.BlockCopy(bodyBytes, 0, frame, 1, bodyBytes.Length);
            frame[1 + bodyBytes.Length] = Etx;
            byte[] bccBytes = Encoding.ASCII.GetBytes(bccText);
            Buffer.BlockCopy(bccBytes, 0, frame, frame.Length - 2, 2);
            return frame;
        }

        private static byte ComputeBcc(byte[] content, byte terminator)
        {
            byte bcc = 0;
            for (int i = 0; i < content.Length; i++)
                bcc ^= content[i];
            bcc ^= terminator;
            return bcc;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _serialPort.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("Mitsubishi ϵ�� �����ѶϿ�");
                offset += read;
            }
            return buffer;
        }

        private static byte[] DecodeWordData(string text, int points)
        {
            int expectedLength = points * 4;
            if (text == null || text.Length < expectedLength)
                throw new InvalidOperationException("Mitsubishi ϵ�� �ֶ�ȡ��Ӧ���Ȳ���");
            if (text.Length > expectedLength)
                text = text.Substring(text.Length - expectedLength);

            byte[] data = new byte[points * 2];
            for (int i = 0; i < points; i++)
            {
                ushort value = ushort.Parse(text.Substring(i * 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte[] bytes = BitConverter.GetBytes(value);
                data[i * 2] = bytes[0];
                data[i * 2 + 1] = bytes[1];
            }
            return data;
        }

        private static string EncodeWordData(byte[] data, int points)
        {
            StringBuilder builder = new StringBuilder(points * 4);
            for (int i = 0; i < points; i++)
            {
                ushort value = BitConverter.ToUInt16(data, i * 2);
                builder.Append(value.ToString("X4", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static bool[] DecodeBitData(string text, int points)
        {
            if (text == null || text.Length < points)
                throw new InvalidOperationException("Mitsubishi ϵ�� λ��ȡ��Ӧ���Ȳ���");
            if (text.Length > points)
                text = text.Substring(text.Length - points);

            bool[] values = new bool[points];
            for (int i = 0; i < points; i++)
                values[i] = text[i] == '1';
            return values;
        }

        private static string EncodeBitData(bool[] values, int points)
        {
            StringBuilder builder = new StringBuilder(points);
            for (int i = 0; i < points; i++)
                builder.Append(values[i] ? '1' : '0');
            return builder.ToString();
        }

        private static int GetWordCountForWrite(byte[] data)
        {
            return (data.Length + 1) / 2;
        }

        private void PreserveOddStringTailByte(PlcDataType dataType, int elementCount, MitsubishiSerialAddress address, byte[] data, int wordCount)
        {
            if (dataType != PlcDataType.String || (elementCount % 2) == 0 || data == null || data.Length < wordCount * 2)
                return;

            byte[] current = ReadWordsSegmented(address, wordCount);
            if (current != null && current.Length >= wordCount * 2)
                data[wordCount * 2 - 1] = current[wordCount * 2 - 1];
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                Connect();
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
