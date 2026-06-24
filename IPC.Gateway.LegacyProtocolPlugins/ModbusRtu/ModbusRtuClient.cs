/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.ModbusRtu
* 项目描述 ：
* 类 名 称 ：ModbusRtuClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.ModbusRtu
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
using System.IO.Ports;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Plc.Communication.ModbusRtu
{
    
    
    
    
    
    
    
    
    
    public sealed class ModbusRtuClient : IPlcClient
    {
        private const int MaxReadCoils = 2000;
        private const int MaxWriteCoils = 1968;
        private const int MaxReadRegisters = 125;
        private const int MaxWriteRegisters = 123;

        private readonly PlcConnectionOptions _options;
        private SerialPort _serialPort;

        public ModbusRtuClient(PlcConnectionOptions options)
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
            get { return PlcProtocol.ModbusRtu; }
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

        public PlcReadResult Read(string addressText, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            if (elementCount <= 0)
                elementCount = 1;

            ModbusAddress address = ModbusAddress.Parse(addressText, dataType);
            if (address.IsBitArea)
            {
                if (!ModbusDataCodec.IsBitType(dataType))
                    throw new NotSupportedException("线圈/离散输入只能按 BOOL、Coil 或 Discrete Input 类型读取。");
                int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                bool[] bits = ReadBits(start.Area, start.Address, count);
                return new PlcReadResult(GetTypeCode(start.Area), GetTypeName(start.Area), ModbusDataCodec.DecodeBits(dataType, bits, count));
            }

            if (ModbusDataCodec.IsBitOnlyType(dataType))
                throw new NotSupportedException("Coil/Discrete Input 类型只能用于 Modbus 位区域地址。");

            if (IsRegisterBitAccess(address, dataType))
            {
                int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                int registerCount = (start.BitIndex + count + 15) / 16;
                byte[] registerBytes = ReadRegisters(start.Area, start.Address, registerCount);
                return new PlcReadResult(GetTypeCode(start.Area), GetTypeName(start.Area) + ".BIT", ModbusDataCodec.DecodeRegisterBits(dataType, registerBytes, start.BitIndex, count));
            }

            bool usesCount = PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String;
            int registerOffset = PlcDataTypeHelper.IsArray(dataType) ? ModbusDataCodec.GetRegisterOffset(dataType, elementOffset) : 0;
            ModbusAddress registerStart = address.OffsetRegisters(registerOffset);
            int registers = ModbusDataCodec.GetRegisterCount(dataType, usesCount ? elementCount : 1);
            byte[] data = ReadRegisters(registerStart.Area, registerStart.Address, registers);
            object value = ModbusDataCodec.DecodeRegisters(dataType, data, usesCount ? elementCount : 1);
            return new PlcReadResult(GetTypeCode(registerStart.Area), GetTypeName(registerStart.Area), value);
        }

        public void Write(string addressText, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();

            ModbusAddress address = ModbusAddress.Parse(addressText, dataType);
            if (address.IsReadOnly || dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                throw new NotSupportedException("Modbus 离散输入和输入寄存器是只读区域，不能写入。");

            if (address.IsBitArea)
            {
                if (!ModbusDataCodec.IsBitType(dataType))
                    throw new NotSupportedException("线圈只能按 BOOL、Coil 或 Coil[] 类型写入。");
                bool[] values = ModbusDataCodec.EncodeBits(dataType, valueText);
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                WriteBits(start.Address, values);
                return;
            }

            if (ModbusDataCodec.IsBitOnlyType(dataType))
                throw new NotSupportedException("Coil/Coil[] 类型只能用于 Modbus 线圈地址。");

            if (IsRegisterBitAccess(address, dataType))
            {
                bool[] values = ModbusDataCodec.EncodeBits(dataType, valueText);
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                int registerCount = (start.BitIndex + values.Length + 15) / 16;
                byte[] current = ReadRegisters(start.Area, start.Address, registerCount);
                ModbusDataCodec.SetRegisterBits(current, start.BitIndex, values);
                WriteRegisters(start.Address, current);
                return;
            }

            int registerOffset = PlcDataTypeHelper.IsArray(dataType) ? ModbusDataCodec.GetRegisterOffset(dataType, elementOffset) : 0;
            ModbusAddress registerStart = address.OffsetRegisters(registerOffset);
            byte[] data = ModbusDataCodec.EncodeRegisters(dataType, valueText);
            WriteRegisters(registerStart.Address, data);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private bool[] ReadBits(ModbusArea area, int startAddress, int count)
        {
            if (area != ModbusArea.Coil && area != ModbusArea.DiscreteInput)
                throw new NotSupportedException("当前地址不是 Modbus 位区域。");

            bool[] result = new bool[count];
            int copied = 0;
            while (copied < count)
            {
                int segmentCount = Math.Min(MaxReadCoils, count - copied);
                byte function = area == ModbusArea.Coil ? (byte)0x01 : (byte)0x02;
                byte[] response = SendRequest(function, BuildAddressCountPdu(function, startAddress + copied, segmentCount));
                if (response.Length < 2)
                    throw new InvalidOperationException("Modbus RTU 位读取响应长度不足。");

                int byteCount = response[1];
                byte[] data = new byte[byteCount];
                Buffer.BlockCopy(response, 2, data, 0, byteCount);
                bool[] segment = ModbusDataCodec.UnpackCoils(data, segmentCount);
                Array.Copy(segment, 0, result, copied, segmentCount);
                copied += segmentCount;
            }
            return result;
        }

        private byte[] ReadRegisters(ModbusArea area, int startAddress, int registerCount)
        {
            if (area != ModbusArea.HoldingRegister && area != ModbusArea.InputRegister)
                throw new NotSupportedException("当前地址不是 Modbus 寄存器区域。");

            MemoryStream stream = new MemoryStream();
            int copied = 0;
            while (copied < registerCount)
            {
                int segmentCount = Math.Min(MaxReadRegisters, registerCount - copied);
                byte function = area == ModbusArea.HoldingRegister ? (byte)0x03 : (byte)0x04;
                byte[] response = SendRequest(function, BuildAddressCountPdu(function, startAddress + copied, segmentCount));
                if (response.Length < 2)
                    throw new InvalidOperationException("Modbus RTU 寄存器读取响应长度不足。");

                int byteCount = response[1];
                if (byteCount < segmentCount * 2 || response.Length < byteCount + 2)
                    throw new InvalidOperationException("Modbus RTU 寄存器读取响应数据长度不足。");

                stream.Write(response, 2, segmentCount * 2);
                copied += segmentCount;
            }
            return stream.ToArray();
        }

        private void WriteBits(int startAddress, bool[] values)
        {
            int written = 0;
            while (written < values.Length)
            {
                int segmentCount = Math.Min(MaxWriteCoils, values.Length - written);
                bool[] segment = new bool[segmentCount];
                Array.Copy(values, written, segment, 0, segmentCount);

                if (segmentCount == 1)
                {
                    byte[] pdu = new byte[5];
                    pdu[0] = 0x05;
                    WriteUInt16(pdu, 1, startAddress + written);
                    WriteUInt16(pdu, 3, segment[0] ? 0xFF00 : 0x0000);
                    SendRequest(0x05, pdu);
                }
                else
                {
                    byte[] packed = ModbusDataCodec.PackCoils(segment, segmentCount);
                    byte[] pdu = new byte[6 + packed.Length];
                    pdu[0] = 0x0F;
                    WriteUInt16(pdu, 1, startAddress + written);
                    WriteUInt16(pdu, 3, segmentCount);
                    pdu[5] = (byte)packed.Length;
                    Buffer.BlockCopy(packed, 0, pdu, 6, packed.Length);
                    SendRequest(0x0F, pdu);
                }

                written += segmentCount;
            }
        }

        private void WriteRegisters(int startAddress, byte[] data)
        {
            if (data.Length % 2 != 0)
            {
                byte[] padded = new byte[data.Length + 1];
                Buffer.BlockCopy(data, 0, padded, 0, data.Length);
                data = padded;
            }

            int totalRegisters = data.Length / 2;
            int written = 0;
            while (written < totalRegisters)
            {
                int segmentRegisters = Math.Min(MaxWriteRegisters, totalRegisters - written);
                if (segmentRegisters == 1)
                {
                    byte[] pdu = new byte[5];
                    pdu[0] = 0x06;
                    WriteUInt16(pdu, 1, startAddress + written);
                    pdu[3] = data[written * 2];
                    pdu[4] = data[written * 2 + 1];
                    SendRequest(0x06, pdu);
                }
                else
                {
                    byte[] pdu = new byte[6 + segmentRegisters * 2];
                    pdu[0] = 0x10;
                    WriteUInt16(pdu, 1, startAddress + written);
                    WriteUInt16(pdu, 3, segmentRegisters);
                    pdu[5] = (byte)(segmentRegisters * 2);
                    Buffer.BlockCopy(data, written * 2, pdu, 6, segmentRegisters * 2);
                    SendRequest(0x10, pdu);
                }

                written += segmentRegisters;
            }
        }

        private byte[] SendRequest(byte expectedFunction, byte[] pdu)
        {
            if (pdu == null || pdu.Length == 0)
                throw new ArgumentException("Modbus RTU PDU 不能为空。", "pdu");

            byte slaveId = GetSlaveId();
            byte[] request = new byte[1 + pdu.Length + 2];
            request[0] = slaveId;
            Buffer.BlockCopy(pdu, 0, request, 1, pdu.Length);
            WriteCrc(request, 0, request.Length - 2);

            _serialPort.DiscardInBuffer();
            _serialPort.Write(request, 0, request.Length);

            byte responseSlave = ReadByte();
            byte function = ReadByte();
            if (responseSlave != slaveId)
                throw new InvalidOperationException("Modbus RTU 响应站号不匹配。");

            byte[] frame;
            if ((function & 0x80) != 0)
            {
                byte exceptionCode = ReadByte();
                byte crcLo = ReadByte();
                byte crcHi = ReadByte();
                frame = new[] { responseSlave, function, exceptionCode, crcLo, crcHi };
                ValidateCrc(frame);
                throw new InvalidOperationException("Modbus异常: 功能码 0x" + function.ToString("X2") + ", 异常码 0x" + exceptionCode.ToString("X2") + " (" + GetExceptionName(exceptionCode) + ")");
            }

            if (function != expectedFunction)
                throw new InvalidOperationException("Modbus RTU 响应功能码不匹配。");

            if (function == 0x01 || function == 0x02 || function == 0x03 || function == 0x04)
            {
                byte byteCount = ReadByte();
                byte[] dataAndCrc = ReadExact(byteCount + 2);
                frame = new byte[3 + dataAndCrc.Length];
                frame[0] = responseSlave;
                frame[1] = function;
                frame[2] = byteCount;
                Buffer.BlockCopy(dataAndCrc, 0, frame, 3, dataAndCrc.Length);
            }
            else
            {
                byte[] rest = ReadExact(6);
                frame = new byte[2 + rest.Length];
                frame[0] = responseSlave;
                frame[1] = function;
                Buffer.BlockCopy(rest, 0, frame, 2, rest.Length);
            }

            ValidateCrc(frame);
            byte[] responsePdu = new byte[frame.Length - 3];
            Buffer.BlockCopy(frame, 1, responsePdu, 0, responsePdu.Length);
            return responsePdu;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _serialPort.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("Modbus RTU 串口连接已断开。");
                offset += read;
            }
            return buffer;
        }

        private byte ReadByte()
        {
            int value = _serialPort.ReadByte();
            if (value < 0)
                throw new IOException("Modbus RTU 串口连接已断开。");
            return (byte)value;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                Connect();
        }

        private byte GetSlaveId()
        {
            int slaveId = _options.Rack <= 0 ? 1 : _options.Rack;
            if (slaveId > 247)
                slaveId = 247;
            return (byte)slaveId;
        }

        private static byte[] BuildAddressCountPdu(byte function, int address, int count)
        {
            byte[] pdu = new byte[5];
            pdu[0] = function;
            WriteUInt16(pdu, 1, address);
            WriteUInt16(pdu, 3, count);
            return pdu;
        }

        private static bool IsRegisterBitAccess(ModbusAddress address, PlcDataType dataType)
        {
            return (dataType == PlcDataType.Bool && address.HasBitIndex) ||
                   dataType == PlcDataType.BoolArray;
        }

        private static ushort GetTypeCode(ModbusArea area)
        {
            switch (area)
            {
                case ModbusArea.Coil:
                    return 0x0001;
                case ModbusArea.DiscreteInput:
                    return 0x0002;
                case ModbusArea.HoldingRegister:
                    return 0x0003;
                case ModbusArea.InputRegister:
                    return 0x0004;
                default:
                    return 0;
            }
        }

        private static string GetTypeName(ModbusArea area)
        {
            switch (area)
            {
                case ModbusArea.Coil:
                    return "Coil";
                case ModbusArea.DiscreteInput:
                    return "Discrete Input";
                case ModbusArea.HoldingRegister:
                    return "Holding Register";
                case ModbusArea.InputRegister:
                    return "Input Register";
                default:
                    return "Unknown";
            }
        }

        private static string GetExceptionName(byte code)
        {
            switch (code)
            {
                case 0x01:
                    return "Illegal Function";
                case 0x02:
                    return "Illegal Data Address";
                case 0x03:
                    return "Illegal Data Value";
                case 0x04:
                    return "Slave Device Failure";
                case 0x05:
                    return "Acknowledge";
                case 0x06:
                    return "Slave Device Busy";
                case 0x0A:
                    return "Gateway Path Unavailable";
                case 0x0B:
                    return "Gateway Target Device Failed To Respond";
                default:
                    return "Unknown";
            }
        }

        private static void WriteUInt16(byte[] data, int offset, int value)
        {
            if (value < 0 || value > 0xFFFF)
                throw new ArgumentOutOfRangeException("value", "Modbus 地址或数量超出 UInt16 范围。");
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)(value & 0xFF);
        }

        private static void WriteCrc(byte[] frame, int offset, int count)
        {
            ushort crc = ComputeCrc(frame, offset, count);
            frame[offset + count] = (byte)(crc & 0xFF);
            frame[offset + count + 1] = (byte)(crc >> 8);
        }

        private static void ValidateCrc(byte[] frame)
        {
            if (frame == null || frame.Length < 4)
                throw new InvalidOperationException("Modbus RTU 响应长度不足。");

            ushort expected = ComputeCrc(frame, 0, frame.Length - 2);
            ushort actual = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
            if (expected != actual)
                throw new InvalidOperationException("Modbus RTU CRC 校验失败。");
        }

        private static ushort ComputeCrc(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }
    }
}
