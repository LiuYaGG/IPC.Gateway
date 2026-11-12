using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.ModbusTcp;

public enum ModbusDataFormat { ABCD, BADC, CDAB, DCBA }

/// <summary>Symmetric numeric byte permutation, independently applied to every array element.</summary>
public static class ModbusRegisterFormat
{
    public static byte[] Transform(byte[] data, PlcDataType type, ModbusDataFormat format)
    {
        ArgumentNullException.ThrowIfNull(data);
        int width = type switch
        {
            PlcDataType.Int16 or PlcDataType.UInt16 or PlcDataType.Int16Array or PlcDataType.UInt16Array => 2,
            PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float or PlcDataType.Int32Array or PlcDataType.UInt32Array or PlcDataType.FloatArray => 4,
            PlcDataType.Int64 or PlcDataType.UInt64 or PlcDataType.Double or PlcDataType.Int64Array or PlcDataType.UInt64Array or PlcDataType.DoubleArray => 8,
            _ => 0
        };
        // Strings and physical register/coil bit positions have their own layout.
        if (width == 0 || format == ModbusDataFormat.ABCD) return data;
        if (data.Length % width != 0) throw new ArgumentException("Modbus numeric data length does not match its element size.", nameof(data));
        var result = new byte[data.Length];
        bool swapBytes = format is ModbusDataFormat.BADC or ModbusDataFormat.DCBA;
        bool reverseWords = format is ModbusDataFormat.CDAB or ModbusDataFormat.DCBA;
        for (int offset = 0; offset < data.Length; offset += width)
        {
            for (int word = 0; word < width / 2; word++)
            {
                int source = offset + (reverseWords ? width / 2 - word - 1 : word) * 2;
                result[offset + word * 2] = data[source + (swapBytes ? 1 : 0)];
                result[offset + word * 2 + 1] = data[source + (swapBytes ? 0 : 1)];
            }
        }
        return result;
    }
}
