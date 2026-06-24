/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiQlSerial
* 项目描述 ：
* 类 名 称 ：MitsubishiQlSerialClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiQlSerial
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

namespace IPC.Plc.Communication.MitsubishiQlSerial
{
    
    
    
    
    
    
    
    
    
    public sealed class MitsubishiQlSerialClient : IPlcClient
    {
        private const byte Enq = 0x05;
        private const byte Stx = 0x02;
        private const byte Etx = 0x03;
        private const byte Ack = 0x06;
        private const byte Nak = 0x15;
        private const int HeaderLength = 10;
        private const int MaxWordPoints = 120;
        private const int MaxBitPoints = 480;

        private readonly PlcConnectionOptions _options;
        private SerialPort _serialPort;

        public MitsubishiQlSerialClient(PlcConnectionOptions options)
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
            get { return PlcProtocol.MitsubishiQlSerial; }
        }

        public void Connect()
        {
            Disconnect();

            string portName = string.IsNullOrWhiteSpace(_options.Host) ? "COM1" : _options.Host.Trim();
            int baudRate = _options.Port <= 0 ? 9600 : _options.Port;
            int dataBits = _options.DataBits <= 0 ? 8 : _options.DataBits;
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

            MitsubishiQlSerialAddress qlAddress = BuildAddress(address, dataType, elementOffset);
            if (dataType == PlcDataType.Bool)
                return ReadBool(qlAddress);
            if (dataType == PlcDataType.BoolArray)
                return ReadBoolArray(qlAddress, elementCount);

            int wordCount = McDataCodec.GetWordCount(dataType, elementCount);
            byte[] data = ReadWordsSegmented(qlAddress, wordCount);
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
            MitsubishiQlSerialAddress qlAddress = BuildAddress(address, dataType, elementOffset);
            byte[] data = McDataCodec.Encode(dataType, valueText, writeElementCount);

            if (dataType == PlcDataType.Bool)
            {
                WriteBool(qlAddress, data[0] != 0);
                return;
            }

            if (dataType == PlcDataType.BoolArray)
            {
                WriteBoolArray(qlAddress, data, writeElementCount);
                return;
            }

            int wordCount = GetWordCountForWrite(data);
            PreserveOddStringTailByte(dataType, writeElementCount, qlAddress, data, wordCount);
            WriteWordsSegmented(qlAddress, data, wordCount);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private MitsubishiQlSerialAddress BuildAddress(string address, PlcDataType dataType, int elementOffset)
        {
            MitsubishiQlSerialAddress parsed = MitsubishiQlSerialAddress.Parse(address);
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray)
                return parsed.AddBitOffset(elementOffset);
            return parsed.AddDeviceOffset(McDataCodec.GetDeviceOffset(dataType, elementOffset));
        }

        private PlcReadResult ReadBool(MitsubishiQlSerialAddress address)
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

        private PlcReadResult ReadBoolArray(MitsubishiQlSerialAddress address, int count)
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

        private void WriteBool(MitsubishiQlSerialAddress address, bool value)
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

        private void WriteBoolArray(MitsubishiQlSerialAddress address, byte[] values, int count)
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

        private byte[] ReadWordsSegmented(MitsubishiQlSerialAddress address, int wordCount)
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

        private void WriteWordsSegmented(MitsubishiQlSerialAddress address, byte[] data, int wordCount)
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

        private bool[] ReadBitsSegmented(MitsubishiQlSerialAddress address, int count)
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

        private void WriteBitsSegmented(MitsubishiQlSerialAddress address, bool[] values, int count)
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

        private byte[] ReadWords(MitsubishiQlSerialAddress address, int points)
        {
            string data = SendRead("0401", "0000", address, points);
            return DecodeWordData(data, points);
        }

        private void WriteWords(MitsubishiQlSerialAddress address, byte[] data, int points)
        {
            SendWrite("1401", "0000", address, points, EncodeWordData(data, points));
        }

        private bool[] ReadBits(MitsubishiQlSerialAddress address, int points)
        {
            string data = SendRead("0401", "0001", address, points);
            return DecodeBitData(data, points);
        }

        private void WriteBits(MitsubishiQlSerialAddress address, bool[] values, int points)
        {
            SendWrite("1401", "0001", address, points, EncodeBitData(values, points));
        }

        private string SendRead(string command, string subcommand, MitsubishiQlSerialAddress address, int points)
        {
            string requestData = BuildRequestData(command, subcommand, address, points, null);
            SendRequest(requestData);
            return ReadDataResponse();
        }

        private void SendWrite(string command, string subcommand, MitsubishiQlSerialAddress address, int points, string data)
        {
            string requestData = BuildRequestData(command, subcommand, address, points, data);
            SendRequest(requestData);
            ReadCompletionResponse();
        }

        private string BuildRequestData(string command, string subcommand, MitsubishiQlSerialAddress address, int points, string data)
        {
            return command +
                   subcommand +
                   address.DeviceCode +
                   address.FormatDeviceNumber() +
                   points.ToString("D4", CultureInfo.InvariantCulture) +
                   (data ?? string.Empty);
        }

        private void SendRequest(string requestData)
        {
            string header = BuildHeader();
            string checksum = ComputeSumText(header + requestData, false);
            string text = header + requestData + checksum;
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            byte[] frame = new byte[bytes.Length + 1];
            frame[0] = Enq;
            Buffer.BlockCopy(bytes, 0, frame, 1, bytes.Length);
            _serialPort.DiscardInBuffer();
            _serialPort.Write(frame, 0, frame.Length);
        }

        private string ReadDataResponse()
        {
            int first = _serialPort.ReadByte();
            if (first == Nak)
                ThrowNak();
            if (first != Stx)
                throw new InvalidOperationException("Mitsubishi Q/L ��Ӧ��ʱ������ 0x" + first.ToString("X2", CultureInfo.InvariantCulture));

            MemoryStream content = new MemoryStream();
            while (true)
            {
                int value = _serialPort.ReadByte();
                if (value < 0)
                    throw new IOException("Mitsubishi Q/L ϵ�� �����ѶϿ�");
                if (value == Etx)
                    break;
                content.WriteByte((byte)value);
            }

            byte[] checksumBytes = ReadExact(2);
            string contentText = Encoding.ASCII.GetString(content.ToArray());
            string expected = ComputeSumText(contentText, true);
            string actual = Encoding.ASCII.GetString(checksumBytes);
            if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Mitsubishi Q/L ϵ�� sum check У��ʧ��");
            if (contentText.Length < HeaderLength)
                throw new InvalidOperationException("Mitsubishi Q/L ϵ�� ��Ӧ���Ȳ���");
            return contentText.Substring(HeaderLength);
        }

        private void ReadCompletionResponse()
        {
            int first = _serialPort.ReadByte();
            if (first == Nak)
                ThrowNak();
            if (first != Ack)
                throw new InvalidOperationException("Mitsubishi Q/L ϵ�� д����Ӧ��ʱ������ 0x" + first.ToString("X2", CultureInfo.InvariantCulture));

            try
            {
                byte[] headerBytes = ReadExact(HeaderLength);
                byte[] checksumBytes = ReadExact(2);
                string header = Encoding.ASCII.GetString(headerBytes);
                string expected = ComputeSumText(header, false);
                string actual = Encoding.ASCII.GetString(checksumBytes);
                if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Mitsubishi Q/L ϵ�� ACK sum check У��ʧ��");
            }
            catch (TimeoutException)
            {
                
            }
        }

        private void ThrowNak()
        {
            string detail = string.Empty;
            try
            {
                byte[] rest = ReadExact(HeaderLength + 4 + 2);
                detail = "��Ӧ " + Encoding.ASCII.GetString(rest);
            }
            catch
            {
            }
            throw new InvalidOperationException("Mitsubishi Q/L ϵ�� PLC ���� NAK" + detail); 
        }

        private string BuildHeader()
        {
            int station = _options.Rack;
            if (station < 0)
                station = 0;
            if (station > 31)
                station = 31;
            return "F9" + station.ToString("X2", CultureInfo.InvariantCulture) + "00FF00";
        }

        private static string ComputeSumText(string text, bool includeEtx)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            int sum = 0;
            for (int i = 0; i < bytes.Length; i++)
                sum += bytes[i];
            if (includeEtx)
                sum += Etx;
            return (sum & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _serialPort.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("Mitsubishi Q/L ϵ�� �����ѶϿ�");
                offset += read;
            }
            return buffer;
        }

        private static byte[] DecodeWordData(string text, int points)
        {
            int expectedLength = points * 4;
            if (text == null || text.Length < expectedLength)
                throw new InvalidOperationException("Mitsubishi Q/L ϵ�� �ֶ�ȡ��Ӧ���Ȳ���");
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
                throw new InvalidOperationException("Mitsubishi Q/L ϵ�� λ��ȡ��Ӧ���Ȳ���");
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

        private void PreserveOddStringTailByte(PlcDataType dataType, int elementCount, MitsubishiQlSerialAddress address, byte[] data, int wordCount)
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
