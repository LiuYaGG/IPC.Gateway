using System.Buffers.Binary;
using System.Text;

namespace IPC.Gateway.Mqtt.Sparkplug;

public static partial class SparkplugPayloadDecoder
{
    public static SparkplugPayload Decode(ReadOnlySpan<byte> bytes)
    {
        SparkplugPayload payload = new SparkplugPayload();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 0)
                payload.Timestamp = FromUnixMilliseconds(ReadVarint(bytes, ref offset));
            else if (field == 2 && wire == 2)
                payload.Metrics.Add(DecodeMetric(ReadLengthDelimited(bytes, ref offset)));
            else if (field == 3 && wire == 0)
                payload.Sequence = checked((uint)ReadVarint(bytes, ref offset));
            else if (field == 4 && wire == 2)
                payload.Uuid = Encoding.UTF8.GetString(ReadLengthDelimited(bytes, ref offset));
            else if (field == 5 && wire == 2)
                payload.Body = ReadLengthDelimited(bytes, ref offset).ToArray();
            else
                Skip(bytes, ref offset, wire);
        }
        return payload;
    }

    private static SparkplugMetric DecodeMetric(ReadOnlySpan<byte> bytes)
    {
        SparkplugMetric metric = new SparkplugMetric();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 2:
                    metric.Name = Encoding.UTF8.GetString(ReadLengthDelimited(bytes, ref offset));
                    break;
                case 2 when wire == 0:
                    metric.Alias = ReadVarint(bytes, ref offset);
                    break;
                case 3 when wire == 0:
                    metric.Timestamp = FromUnixMilliseconds(ReadVarint(bytes, ref offset));
                    break;
                case 4 when wire == 0:
                    metric.DataType = (SparkplugDataType)checked((uint)ReadVarint(bytes, ref offset));
                    break;
                case 5 when wire == 0:
                    metric.IsHistorical = ReadVarint(bytes, ref offset) != 0;
                    break;
                case 6 when wire == 0:
                    metric.IsTransient = ReadVarint(bytes, ref offset) != 0;
                    break;
                case 7 when wire == 0:
                    metric.IsNull = ReadVarint(bytes, ref offset) != 0;
                    break;
                case 8 when wire == 2:
                    metric.MetaData = DecodeMetaData(ReadLengthDelimited(bytes, ref offset));
                    break;
                case 9 when wire == 2:
                    CopyProperties(DecodePropertySet(ReadLengthDelimited(bytes, ref offset)), metric);
                    break;
                case 10 when wire == 0:
                    metric.Value = DecodeIntValue(metric.DataType, ReadVarint(bytes, ref offset));
                    break;
                case 11 when wire == 0:
                    metric.Value = DecodeLongValue(metric.DataType, ReadVarint(bytes, ref offset));
                    break;
                case 12 when wire == 5:
                    metric.Value = ReadSingle(bytes, ref offset);
                    break;
                case 13 when wire == 1:
                    metric.Value = ReadDouble(bytes, ref offset);
                    break;
                case 14 when wire == 0:
                    metric.Value = ReadVarint(bytes, ref offset) != 0;
                    break;
                case 15 when wire == 2:
                    metric.Value = Encoding.UTF8.GetString(ReadLengthDelimited(bytes, ref offset));
                    break;
                case 16 when wire == 2:
                    metric.Value = ReadLengthDelimited(bytes, ref offset).ToArray();
                    break;
                case 17 when wire == 2:
                    metric.DataSetValue = DecodeDataSet(ReadLengthDelimited(bytes, ref offset));
                    metric.Value = IsArrayDataType(metric.DataType)
                        ? DecodeArrayValue(metric.DataSetValue)
                        : metric.DataSetValue;
                    break;
                case 18 when wire == 2:
                    metric.TemplateValue = DecodeTemplate(ReadLengthDelimited(bytes, ref offset));
                    metric.Value = metric.TemplateValue;
                    break;
                case 19 when wire == 2:
                    metric.Value = DecodePropertySet(ReadLengthDelimited(bytes, ref offset));
                    break;
                case 20 when wire == 2:
                    metric.Value = DecodePropertySetList(ReadLengthDelimited(bytes, ref offset));
                    break;
                default:
                    Skip(bytes, ref offset, wire);
                    break;
            }
        }
        if (metric.IsNull)
            metric.Value = null;
        return metric;
    }

    private static object DecodeIntValue(SparkplugDataType dataType, ulong value)
    {
        uint raw = unchecked((uint)value);
        return dataType switch
        {
            SparkplugDataType.Int8 => unchecked((sbyte)raw),
            SparkplugDataType.Int16 => unchecked((short)raw),
            SparkplugDataType.Int32 => unchecked((int)raw),
            SparkplugDataType.UInt8 => unchecked((byte)raw),
            SparkplugDataType.UInt16 => unchecked((ushort)raw),
            _ => raw
        };
    }

    private static object DecodeLongValue(SparkplugDataType dataType, ulong value)
    {
        return dataType == SparkplugDataType.Int64 ? unchecked((long)value) : value;
    }

    private static DateTimeOffset FromUnixMilliseconds(ulong value)
    {
        return value <= long.MaxValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)value) : DateTimeOffset.MaxValue;
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> bytes, ref int offset)
    {
        int length = checked((int)ReadVarint(bytes, ref offset));
        EnsureAvailable(bytes, offset, length);
        ReadOnlySpan<byte> result = bytes.Slice(offset, length);
        offset += length;
        return result;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            EnsureAvailable(bytes, offset, 1);
            byte current = bytes[offset++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
                return value;
        }
        throw new FormatException("Sparkplug B varint 过长。");
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, ref int offset)
    {
        EnsureAvailable(bytes, offset, 4);
        float value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static double ReadDouble(ReadOnlySpan<byte> bytes, ref int offset)
    {
        EnsureAvailable(bytes, offset, 8);
        double value = BinaryPrimitives.ReadDoubleLittleEndian(bytes.Slice(offset, 8));
        offset += 8;
        return value;
    }

    private static void Skip(ReadOnlySpan<byte> bytes, ref int offset, int wire)
    {
        if (wire == 0) _ = ReadVarint(bytes, ref offset);
        else if (wire == 1) { EnsureAvailable(bytes, offset, 8); offset += 8; }
        else if (wire == 2) _ = ReadLengthDelimited(bytes, ref offset);
        else if (wire == 5) { EnsureAvailable(bytes, offset, 4); offset += 4; }
        else throw new FormatException("Sparkplug B 包含不支持的 wire type " + wire + "。");
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > bytes.Length - count)
            throw new FormatException("Sparkplug B payload 已截断。");
    }
}
