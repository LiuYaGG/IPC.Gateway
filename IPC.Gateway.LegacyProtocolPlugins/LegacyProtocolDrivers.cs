/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.LegacyProtocolPlugins
* 项目描述 ：
* 类 名 称 ：RockwellCipProtocolDriver
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.LegacyProtocolPlugins
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
using System.Text.RegularExpressions;
using IPC.Plc.Communication.Ads;
using IPC.Plc.Communication.Bacnet;
using IPC.Plc.Communication.CanOpen;
using IPC.Plc.Communication.Cip;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Dnp3;
using IPC.Plc.Communication.MitsubishiMc;
using IPC.Plc.Communication.MitsubishiMc1E;
using IPC.Plc.Communication.MitsubishiQlSerial;
using IPC.Plc.Communication.MitsubishiSerial;
using IPC.Plc.Communication.ModbusAscii;
using IPC.Plc.Communication.ModbusRtu;
using IPC.Plc.Communication.Mqtt;
using IPC.Plc.Communication.OmronFins;
using IPC.Plc.Communication.OpcDa;
using IPC.Plc.Communication.OpcUa;
using IPC.Plc.Communication.Pccc;
using IPC.Plc.Communication.SiemensS7;
using IPC.Plc.Communication.Snmp;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    public sealed class Dnp3ProtocolDriver : LegacyProtocolDriver
    {
        public Dnp3ProtocolDriver()
            : base("legacy.dnp3", "DNP3 TCP Master", PlcProtocol.Dnp3)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options) => new Dnp3Client(options);

        public override PlcTagValidationResult ValidateTag(PlcConnectionOptions options, string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                Dnp3Address parsed = Dnp3Address.Parse(normalized);
                if (elementCount != 1 || elementOffset != 0)
                    return PlcTagValidationResult.Invalid(normalized, "DNP3 点位不使用元素数量或偏移。");
                _ = Dnp3ValueCodec.ConvertValue(parsed.PointType == Dnp3PointType.Binary ? false : 0, dataType);
                return PlcTagValidationResult.Valid(parsed.ToString());
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class BeckhoffAdsProtocolDriver : LegacyProtocolDriver
    {
        public BeckhoffAdsProtocolDriver()
            : base("legacy.beckhoff-ads", "Beckhoff TwinCAT ADS", PlcProtocol.BeckhoffAds)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new BeckhoffAdsClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                _ = AdsAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量或偏移无效。");
                _ = AdsDataCodec.GetManagedType(dataType);
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class SnmpProtocolDriver : LegacyProtocolDriver
    {
        public SnmpProtocolDriver()
            : base("legacy.snmp", "SNMP v1/v2c/v3", PlcProtocol.Snmp)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new SnmpClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                normalized = SnmpAddress.Parse(normalized);
                if (elementCount != 1 || elementOffset != 0)
                    return PlcTagValidationResult.Invalid(normalized, "SNMP OID 不使用元素数量或偏移。");
                if (PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.Coil || dataType == PlcDataType.DiscreteInput)
                    return PlcTagValidationResult.Invalid(normalized, "SNMP 不支持该数据类型。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class MqttSouthboundProtocolDriver : LegacyProtocolDriver
    {
        public MqttSouthboundProtocolDriver()
            : base("legacy.mqtt-southbound", "MQTT / Sparkplug B 南向", PlcProtocol.MqttClient)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new MqttSouthboundClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                MqttTagAddress parsed = MqttTagAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(parsed.CacheKey, "元素数量或偏移无效。");
                if (dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                    dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                    return PlcTagValidationResult.Invalid(parsed.CacheKey, "MQTT 请使用 Bool 或 BoolArray，不支持 Modbus 位类型。");
                return PlcTagValidationResult.Valid(parsed.CacheKey);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class RockwellCipProtocolDriver : LegacyProtocolDriver
    {
        public RockwellCipProtocolDriver()
            : base("legacy.rockwell-cip", "Rockwell CIP", PlcProtocol.RockwellCip)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new CipClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            if (!CipExplicitAddress.IsExplicit(normalized))
                return base.ValidateTag(options, normalized, dataType, elementCount, elementOffset);

            try
            {
                _ = CipExplicitAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移无效。");
                if (dataType == PlcDataType.String && elementOffset != 0)
                    return PlcTagValidationResult.Invalid(normalized, "CIP 字符串属性不支持元素偏移。");
                if (dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                    dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                    return PlcTagValidationResult.Invalid(normalized, "通用 CIP 对象属性不支持 Modbus 位类型。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class EtherNetIpProtocolDriver : LegacyProtocolDriver
    {
        public EtherNetIpProtocolDriver()
            : base("legacy.ethernet-ip", "EtherNet/IP", PlcProtocol.EtherNetIp)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new EtherNetIpClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                if (EtherNetIpIoAddress.IsIoAddress(normalized))
                {
                    EtherNetIpDriverOptions driverOptions = EtherNetIpDriverOptions.Parse(options.DriverOptionsJson);
                    if (!driverOptions.UsesImplicitIo)
                        return PlcTagValidationResult.Invalid(normalized, "Input/Output 周期地址需要把 I/O 模式设置为 Implicit。");
                    EtherNetIpIoAddress ioAddress = EtherNetIpIoAddress.Parse(normalized);
                    if (elementCount <= 0 || elementOffset < 0)
                        return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移必须有效。");
                    if (ioAddress.BitOffset.HasValue && dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                        return PlcTagValidationResult.Invalid(normalized, "带位偏移的周期 I/O 地址只支持 Bool 或 BoolArray。");
                    int configuredLength = ioAddress.Direction == EtherNetIpIoDirection.Input
                        ? driverOptions.InputLength
                        : driverOptions.OutputLength;
                    if (configuredLength > 0)
                    {
                        int lastExclusive;
                        if (ioAddress.BitOffset.HasValue)
                        {
                            int bitCount = dataType == PlcDataType.BoolArray ? elementCount : 1;
                            int lastBit = checked(ioAddress.BitOffset.Value + elementOffset + bitCount - 1);
                            lastExclusive = checked(ioAddress.ByteOffset + lastBit / 8 + 1);
                        }
                        else
                        {
                            int scalarSize = dataType == PlcDataType.String ? 1 : Math.Max(1, PlcDataTypeHelper.GetElementSize(dataType));
                            int valueCount = dataType == PlcDataType.String ? elementCount : PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                            lastExclusive = checked(ioAddress.ByteOffset + elementOffset * scalarSize + valueCount * scalarSize);
                        }
                        if (lastExclusive > configuredLength)
                            return PlcTagValidationResult.Invalid(normalized, "周期 I/O 标签超出已配置的 Assembly 数据长度。");
                    }
                    return PlcTagValidationResult.Valid(normalized);
                }

                normalized = EtherNetIpAddress.Normalize(normalized);
                _ = CipExplicitAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移必须有效。");
                if (dataType == PlcDataType.String && elementOffset != 0)
                    return PlcTagValidationResult.Invalid(normalized, "CIP 字符串属性不支持元素偏移。");
                if (dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                    dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                    return PlcTagValidationResult.Invalid(normalized, "EtherNet/IP 对象属性请使用 Bool/BoolArray，不支持 Modbus 位类型。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class RockwellPcccProtocolDriver : LegacyProtocolDriver
    {
        public RockwellPcccProtocolDriver()
            : base("legacy.rockwell-pccc", "Rockwell PCCC", PlcProtocol.RockwellPccc)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new PcccClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                PcccAddress parsed = PcccAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移无效。");
                if (parsed.BitNumber.HasValue && dataType != PlcDataType.Bool)
                    return PlcTagValidationResult.Invalid(normalized, "PCCC位地址必须使用Bool数据类型。");
                if (parsed.FileTypeName == "ST" && dataType != PlcDataType.String)
                    return PlcTagValidationResult.Invalid(normalized, "PCCC ST文件必须使用String数据类型。");
                if (dataType == PlcDataType.Int64 || dataType == PlcDataType.UInt64 || dataType == PlcDataType.Double ||
                    dataType == PlcDataType.Int64Array || dataType == PlcDataType.UInt64Array || dataType == PlcDataType.DoubleArray ||
                    dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                    dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                    return PlcTagValidationResult.Invalid(normalized, "PCCC不支持该数据类型。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class SiemensS7ProtocolDriver : LegacyProtocolDriver
    {
        public SiemensS7ProtocolDriver()
            : base("legacy.siemens-s7", "Siemens S7", PlcProtocol.SiemensS7)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new S7Client(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                _ = S7Address.Parse(normalized, dataType);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量或偏移无效。");
                if ((dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                     dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray))
                    return PlcTagValidationResult.Invalid(normalized, "Siemens S7 请使用 Bool 或 BoolArray，不支持 Modbus 位类型。");
                bool hasBitSuffix = Regex.IsMatch(normalized, @"(?:DBX|[MIQVEA]X?)[0-9]+\.[0-9]+$", RegexOptions.IgnoreCase);
                if (hasBitSuffix && dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                    return PlcTagValidationResult.Invalid(normalized, "带 .bit 后缀的 S7 地址必须使用 Bool 或 BoolArray。" );
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class MitsubishiMcProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiMcProtocolDriver()
            : base("legacy.mitsubishi-mc", "Mitsubishi MC", PlcProtocol.MitsubishiMc)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new McClient(options);
        }

        public override PlcTagValidationResult ValidateTag(PlcConnectionOptions options, string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                _ = McAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移无效。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class MitsubishiMc1EProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiMc1EProtocolDriver()
            : base("legacy.mitsubishi-mc-1e", "Mitsubishi MC 1E", PlcProtocol.MitsubishiMc1E)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new Mc1EClient(options);
        }

        public override PlcTagValidationResult ValidateTag(PlcConnectionOptions options, string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                _ = Mc1EAddress.Parse(normalized);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移无效。");
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class MitsubishiSerialProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiSerialProtocolDriver()
            : base("legacy.mitsubishi-serial", "Mitsubishi FX Serial", PlcProtocol.MitsubishiSerial)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new MitsubishiSerialClient(options);
        }
    }

    public sealed class MitsubishiQlSerialProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiQlSerialProtocolDriver()
            : base("legacy.mitsubishi-ql-serial", "Mitsubishi Q/L Serial", PlcProtocol.MitsubishiQlSerial)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new MitsubishiQlSerialClient(options);
        }
    }

    public sealed class OmronFinsProtocolDriver : LegacyProtocolDriver
    {
        public OmronFinsProtocolDriver()
            : base("legacy.omron-fins", "Omron FINS", PlcProtocol.OmronFins)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new FinsClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量或偏移无效。");
                if (dataType == PlcDataType.Coil || dataType == PlcDataType.CoilArray ||
                    dataType == PlcDataType.DiscreteInput || dataType == PlcDataType.DiscreteInputArray)
                    return PlcTagValidationResult.Invalid(normalized, "FINS 请使用 Bool 或 BoolArray，不支持 Modbus 位类型。");

                FinsDriverOptions driverOptions = FinsDriverOptions.Parse(options);
                FinsAddress parsed = FinsAddress.Parse(normalized, dataType, driverOptions);
                if (FinsDataCodec.IsBitType(dataType))
                {
                    int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                    FinsAddress start = parsed.OffsetBits(PlcDataTypeHelper.IsArray(dataType) ? elementOffset : 0);
                    start.EnsureRange(count, true);
                }
                else
                {
                    if (parsed.HasBitIndex)
                        return PlcTagValidationResult.Invalid(normalized, "非 Bool 类型不能使用 FINS 位地址。");
                    int offset = PlcDataTypeHelper.IsArray(dataType)
                        ? FinsDataCodec.GetWordOffset(dataType, elementOffset)
                        : 0;
                    int count = PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String
                        ? elementCount
                        : 1;
                    FinsAddress start = parsed.OffsetWords(offset);
                    start.EnsureRange(FinsDataCodec.GetWordCount(dataType, count), false);
                }

                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is FormatException ||
                ex is OverflowException ||
                ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class ModbusRtuProtocolDriver : LegacyProtocolDriver
    {
        public ModbusRtuProtocolDriver()
            : base("legacy.modbus-rtu", "Modbus RTU", PlcProtocol.ModbusRtu)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new ModbusRtuClient(options);
        }
    }

    public sealed class ModbusAsciiProtocolDriver : LegacyProtocolDriver
    {
        public ModbusAsciiProtocolDriver()
            : base("legacy.modbus-ascii", "Modbus ASCII", PlcProtocol.ModbusAscii)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new ModbusAsciiClient(options);
        }
    }

    public sealed class BacnetIpProtocolDriver : LegacyProtocolDriver
    {
        public BacnetIpProtocolDriver()
            : base("legacy.bacnet-ip", "BACnet/IP", PlcProtocol.BacnetIp)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new BacnetIpClient(options);
        }
    }

    public sealed class CanOpenProtocolDriver : LegacyProtocolDriver
    {
        public CanOpenProtocolDriver()
            : base("legacy.canopen", "CANopen", PlcProtocol.CanOpen)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new CanOpenClient(options);
        }

        public override PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalized = (address ?? string.Empty).Trim();
            try
            {
                CanOpenDriverOptions driverOptions = CanOpenDriverOptions.Parse(options.DriverOptionsJson);
                CanOpenServiceAddress parsed = CanOpenServiceAddress.Parse(normalized, driverOptions.DefaultNodeId);
                if (elementCount <= 0 || elementOffset < 0)
                    return PlcTagValidationResult.Invalid(normalized, "元素数量和偏移必须有效。");
                if (parsed.BitOffset.HasValue && dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                    return PlcTagValidationResult.Invalid(normalized, "带位偏移的 PDO 地址只支持 Bool 或 BoolArray。");
                if (parsed.BitOffset.HasValue)
                {
                    int bitCount = dataType == PlcDataType.BoolArray ? elementCount : 1;
                    int lastBit = checked(parsed.BitOffset.Value + elementOffset + bitCount - 1);
                    if (parsed.ByteOffset + lastBit / 8 >= 8)
                        return PlcTagValidationResult.Invalid(normalized, "PDO 位标签超出 8 字节标准帧范围。");
                }
                if ((parsed.Kind == CanOpenServiceKind.Heartbeat || parsed.Kind == CanOpenServiceKind.Emergency) &&
                    PlcDataTypeHelper.IsArray(dataType))
                    return PlcTagValidationResult.Invalid(normalized, "Heartbeat 和 EMCY 状态只支持标量标签。");
                if ((parsed.Kind == CanOpenServiceKind.Tpdo || parsed.Kind == CanOpenServiceKind.Rpdo) && !parsed.BitOffset.HasValue)
                {
                    int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
                    int byteCount = dataType == PlcDataType.String ? elementCount : checked(CanOpenDataCodec.GetScalarByteCount(dataType) * count);
                    if (parsed.ByteOffset + elementOffset * Math.Max(1, CanOpenDataCodec.GetScalarByteCount(dataType)) + byteCount > 8)
                        return PlcTagValidationResult.Invalid(normalized, "PDO 标签超出 8 字节标准帧范围。");
                }
                return PlcTagValidationResult.Valid(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is NotSupportedException)
            {
                return PlcTagValidationResult.Invalid(normalized, ex.Message);
            }
        }
    }

    public sealed class OpcUaProtocolDriver : LegacyProtocolDriver
    {
        public OpcUaProtocolDriver()
            : base("legacy.opc-ua", "OPC UA", PlcProtocol.OpcUa)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new OpcUaClient(options);
        }
    }

    public sealed class OpcDaProtocolDriver : LegacyProtocolDriver
    {
        public OpcDaProtocolDriver()
            : base("legacy.opc-da", "OPC DA", PlcProtocol.OpcDa)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new OpcDaClient(options);
        }
    }

    public abstract class LegacyProtocolDriver : IProtocolDriver, IProtocolDriverMetadata, IPlcClientCapabilityProvider, IPlcTagDefinitionValidator
    {
        protected LegacyProtocolDriver(string driverId, string displayName, PlcProtocol protocol)
        {
            DriverId = driverId;
            DisplayName = displayName;
            Protocol = protocol;
        }

        public string DriverId { get; private set; }

        public string DisplayName { get; private set; }

        public PlcProtocol Protocol { get; private set; }

        public virtual bool Supports(PlcConnectionOptions options)
        {
            if (options == null)
                return false;

            if (string.Equals(options.DriverId, DriverId, StringComparison.OrdinalIgnoreCase))
                return true;

            return options.Protocol == Protocol && string.IsNullOrWhiteSpace(options.DriverId);
        }

        public abstract IPlcClient CreateClient(PlcConnectionOptions options);

        public virtual IList<PlcConnectionParameterDefinition> GetConnectionParameters()
        {
            return PlcConnectionParameterCatalog.ForProtocol(Protocol);
        }

        public virtual PlcClientCapabilities GetCapabilities()
        {
            return PlcClientCapabilityCatalog.ForProtocol(Protocol);
        }

        public virtual PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            return PlcProtocolTagValidator.Validate(Protocol, address, dataType, elementCount, elementOffset);
        }
    }
}
