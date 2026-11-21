using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Runtime.Configuration;

// Positions are comparable only within the same device and area. End is exclusive.
public sealed record TagAddressFootprint(string Area, long Start, long End, bool ReadOnly)
{
    public static TagAddressFootprint? Inspect(DeviceConfig device, TagConfig tag)
    {
        var type = tag.DataType;
        bool array = PlcDataTypeHelper.IsArray(type);
        int count = array || type == PlcDataType.String ? tag.ElementCount : 1;
        string address = ProjectConfigValidator.ResolveTagAddress(device, tag);
        var driver = device.Connection.DriverId;
        if (!string.IsNullOrWhiteSpace(driver) && !driver.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) &&
            !driver.StartsWith("legacy.", StringComparison.OrdinalIgnoreCase)) return null;
        if (device.Protocol is PlcProtocol.ModbusTcp or PlcProtocol.ModbusUdp or PlcProtocol.ModbusAscii or PlcProtocol.ModbusRtu)
        {
            var first = ModbusAddress.Parse(address, type);
            if (first.IsBitArea)
            {
                if (!ModbusDataCodec.IsBitType(type)) throw new FormatException("线圈和离散输入只支持位类型。");
                long start = (long)first.Address + (array ? tag.ElementOffset : 0);
                return Modbus(start, count, 65536);
            }
            if (ModbusDataCodec.IsBitOnlyType(type)) throw new FormatException("此数据类型需要位区域地址。");
            bool bits = type == PlcDataType.BoolArray || type == PlcDataType.Bool && first.HasBitIndex;
            if (first.HasBitIndex && !bits) throw new FormatException("带位号的地址需要布尔类型。");
            if (bits)
                return Modbus((long)first.Address * 16 + Math.Max(0, first.BitIndex) + (array ? tag.ElementOffset : 0), count, 65536L * 16);
            long word = (long)first.Address + (array ? ModbusDataCodec.GetRegisterOffset(type, tag.ElementOffset) : 0);
            return Modbus(word * 16, ModbusDataCodec.GetRegisterCount(type, count) * 16L, 65536L * 16);

            TagAddressFootprint Modbus(long start, long length, long limit)
            {
                if (start + length > limit) throw new FormatException("数据范围超出 Modbus 地址空间。");
                return new(first.Area.ToString(), start, start + length, first.IsReadOnly);
            }
        }
        if (device.Protocol == PlcProtocol.SiemensS7 && PlcDriverPluginRegistry.TryValidateTag(device.Connection,
            device.Protocol, address, type, tag.ElementCount, tag.ElementOffset, out var result) &&
            result.IsValid && result.ReadArea != null)
        {
            long length = type switch
            {
                PlcDataType.Bool or PlcDataType.BoolArray => count,
                PlcDataType.String => (count + 2L) * 8,
                _ => (long)PlcDataTypeHelper.GetElementSize(type) * count * 8
            };
            if (result.ReadPosition + length > (1L << 24)) throw new FormatException("数据范围超出 S7 地址空间。");
            return new(result.ReadArea, result.ReadPosition, result.ReadPosition + length, false);
        }
        return null;
    }
}
