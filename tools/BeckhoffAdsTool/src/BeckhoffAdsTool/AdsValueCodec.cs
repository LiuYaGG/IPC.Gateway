using System.Globalization;
using System.Text;

namespace BeckhoffAdsTool;

public enum AdsValueType
{
    Bool,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    String,
    BoolArray,
    Int8Array,
    UInt8Array,
    Int16Array,
    UInt16Array,
    Int32Array,
    UInt32Array,
    Int64Array,
    UInt64Array,
    FloatArray,
    DoubleArray
}

/// <summary>
/// TwinCAT 标量和一维数组的小端字节编码。STRING 使用 ASCII 和末尾零字节。
/// </summary>
public static class AdsValueCodec
{
    public static bool IsArray(AdsValueType type) =>
        type.ToString().EndsWith("Array", StringComparison.Ordinal);

    public static int GetReadSize(AdsValueType type, int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (type == AdsValueType.String)
            return checked(count + 1);
        int elementSize = GetElementSize(GetScalarType(type));
        return IsArray(type) ? checked(count * elementSize) : elementSize;
    }

    public static byte[] Encode(AdsValueType type, string? value, int stringLength)
    {
        if (type == AdsValueType.String)
        {
            byte[] source = Encoding.ASCII.GetBytes(value ?? string.Empty);
            if (source.Length > stringLength)
                throw new FormatException($"字符串超过设定的 {stringLength} 字节长度。");
            byte[] data = new byte[checked(stringLength + 1)];
            Buffer.BlockCopy(source, 0, data, 0, source.Length);
            return data;
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (IsArray(type))
        {
            string[] parts = (value ?? string.Empty).Split(
                new[] { ',', ';', ' ', '\r', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                throw new FormatException("数组至少需要一个元素，多个元素请用逗号分隔。");
            AdsValueType scalarType = GetScalarType(type);
            foreach (string part in parts)
                WriteScalar(writer, scalarType, part);
        }
        else
        {
            WriteScalar(writer, type, (value ?? string.Empty).Trim());
        }
        writer.Flush();
        return stream.ToArray();
    }

    public static string Decode(AdsValueType type, byte[] data, int count)
    {
        int expectedSize = GetReadSize(type, count);
        if (data.Length < expectedSize)
            throw new InvalidOperationException($"ADS 返回长度不足：需要 {expectedSize} 字节，实际 {data.Length} 字节。");
        if (type == AdsValueType.String)
        {
            int length = Array.IndexOf(data, (byte)0, 0, count);
            if (length < 0)
                length = count;
            return Encoding.ASCII.GetString(data, 0, length);
        }

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        AdsValueType scalarType = GetScalarType(type);
        int elementCount = IsArray(type) ? count : 1;
        var values = new string[elementCount];
        for (int i = 0; i < elementCount; i++)
            values[i] = ReadScalar(reader, scalarType);
        return string.Join(", ", values);
    }

    private static AdsValueType GetScalarType(AdsValueType type) => type switch
    {
        AdsValueType.BoolArray => AdsValueType.Bool,
        AdsValueType.Int8Array => AdsValueType.Int8,
        AdsValueType.UInt8Array => AdsValueType.UInt8,
        AdsValueType.Int16Array => AdsValueType.Int16,
        AdsValueType.UInt16Array => AdsValueType.UInt16,
        AdsValueType.Int32Array => AdsValueType.Int32,
        AdsValueType.UInt32Array => AdsValueType.UInt32,
        AdsValueType.Int64Array => AdsValueType.Int64,
        AdsValueType.UInt64Array => AdsValueType.UInt64,
        AdsValueType.FloatArray => AdsValueType.Float,
        AdsValueType.DoubleArray => AdsValueType.Double,
        _ => type
    };

    private static int GetElementSize(AdsValueType type) => type switch
    {
        AdsValueType.Bool or AdsValueType.Int8 or AdsValueType.UInt8 => 1,
        AdsValueType.Int16 or AdsValueType.UInt16 => 2,
        AdsValueType.Int32 or AdsValueType.UInt32 or AdsValueType.Float => 4,
        AdsValueType.Int64 or AdsValueType.UInt64 or AdsValueType.Double => 8,
        _ => throw new NotSupportedException($"不支持的数据类型：{type}")
    };

    private static void WriteScalar(BinaryWriter writer, AdsValueType type, string text)
    {
        switch (type)
        {
            case AdsValueType.Bool: writer.Write(ParseBoolean(text) ? (byte)1 : (byte)0); break;
            case AdsValueType.Int8: writer.Write(sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.UInt8: writer.Write(byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.Int16: writer.Write(short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.UInt16: writer.Write(ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.Int32: writer.Write(int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.UInt32: writer.Write(uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.Int64: writer.Write(long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.UInt64: writer.Write(ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case AdsValueType.Float: writer.Write(float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)); break;
            case AdsValueType.Double: writer.Write(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)); break;
            default: throw new NotSupportedException($"不支持写入类型：{type}");
        }
    }

    private static string ReadScalar(BinaryReader reader, AdsValueType type)
    {
        return type switch
        {
            AdsValueType.Bool => reader.ReadBoolean().ToString(),
            AdsValueType.Int8 => reader.ReadSByte().ToString(CultureInfo.InvariantCulture),
            AdsValueType.UInt8 => reader.ReadByte().ToString(CultureInfo.InvariantCulture),
            AdsValueType.Int16 => reader.ReadInt16().ToString(CultureInfo.InvariantCulture),
            AdsValueType.UInt16 => reader.ReadUInt16().ToString(CultureInfo.InvariantCulture),
            AdsValueType.Int32 => reader.ReadInt32().ToString(CultureInfo.InvariantCulture),
            AdsValueType.UInt32 => reader.ReadUInt32().ToString(CultureInfo.InvariantCulture),
            AdsValueType.Int64 => reader.ReadInt64().ToString(CultureInfo.InvariantCulture),
            AdsValueType.UInt64 => reader.ReadUInt64().ToString(CultureInfo.InvariantCulture),
            AdsValueType.Float => reader.ReadSingle().ToString("G9", CultureInfo.InvariantCulture),
            AdsValueType.Double => reader.ReadDouble().ToString("G17", CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"不支持读取类型：{type}")
        };
    }

    private static bool ParseBoolean(string text)
    {
        if (bool.TryParse(text, out bool value))
            return value;
        if (text == "1") return true;
        if (text == "0") return false;
        throw new FormatException("布尔值请输入 true、false、1 或 0。");
    }
}
