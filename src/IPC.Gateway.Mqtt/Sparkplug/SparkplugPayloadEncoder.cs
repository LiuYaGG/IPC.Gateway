/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Mqtt.Sparkplug
* 项目描述 ：
* 类 名 称 ：SparkplugPayloadEncoder
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Mqtt.Sparkplug
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
using System.Buffers.Binary;
using System.Text;

namespace IPC.Gateway.Mqtt.Sparkplug;

public static partial class SparkplugPayloadEncoder
{
    public static byte[] Encode(SparkplugPayload payload)
    {
        payload ??= new SparkplugPayload();
        using MemoryStream stream = new MemoryStream();

        WriteUInt64Field(stream, 1, ToUnixMilliseconds(payload.Timestamp));
        for (int i = 0; i < payload.Metrics.Count; i++)
        {
            byte[] metric = EncodeMetric(payload.Metrics[i]);
            WriteBytesField(stream, 2, metric);
        }
        WriteUInt64Field(stream, 3, payload.Sequence);
        if (!string.IsNullOrWhiteSpace(payload.Uuid))
            WriteStringField(stream, 4, payload.Uuid);
        if (payload.Body.Length > 0)
            WriteBytesField(stream, 5, payload.Body);

        return stream.ToArray();
    }

    private static byte[] EncodeMetric(SparkplugMetric metric)
    {
        metric ??= new SparkplugMetric();
        using MemoryStream stream = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(metric.Name))
            WriteStringField(stream, 1, metric.Name);
        if (metric.Alias.HasValue)
            WriteUInt64Field(stream, 2, metric.Alias.Value);

        WriteUInt64Field(stream, 3, ToUnixMilliseconds(metric.Timestamp));
        WriteUInt64Field(stream, 4, (uint)metric.DataType);
        if (metric.IsHistorical)
            WriteBoolField(stream, 5, true);
        if (metric.IsTransient)
            WriteBoolField(stream, 6, true);
        if (metric.IsNull)
            WriteBoolField(stream, 7, true);
        if (metric.MetaData != null)
            WriteBytesField(stream, 8, EncodeMetaData(metric.MetaData));
        if (metric.Properties.Count > 0 || metric.TypedProperties.Values.Count > 0)
            WriteBytesField(stream, 9, EncodeProperties(metric));

        if (!metric.IsNull)
            WriteMetricValue(stream, metric);

        return stream.ToArray();
    }

    private static void WriteMetricValue(Stream stream, SparkplugMetric metric)
    {
        object? value = metric.Value;
        switch (metric.DataType)
        {
            case SparkplugDataType.Boolean:
                WriteBoolField(stream, 14, ToBoolean(value));
                break;
            case SparkplugDataType.Float:
                WriteFloatField(stream, 12, ToSingle(value));
                break;
            case SparkplugDataType.Double:
                WriteDoubleField(stream, 13, ToDouble(value));
                break;
            case SparkplugDataType.Int8:
            case SparkplugDataType.Int16:
            case SparkplugDataType.Int32:
                WriteUInt64Field(stream, 10, unchecked((uint)ToInt32(value)));
                break;
            case SparkplugDataType.UInt8:
            case SparkplugDataType.UInt16:
            case SparkplugDataType.UInt32:
                WriteUInt64Field(stream, 10, ToUInt32(value));
                break;
            case SparkplugDataType.Int64:
                WriteUInt64Field(stream, 11, unchecked((ulong)ToInt64(value)));
                break;
            case SparkplugDataType.UInt64:
            case SparkplugDataType.DateTime:
                WriteUInt64Field(stream, 11, ToUInt64(value));
                break;
            case SparkplugDataType.Bytes:
            case SparkplugDataType.File:
                WriteBytesField(stream, 16, value as byte[] ?? Array.Empty<byte>());
                break;
            case SparkplugDataType.DataSet:
                WriteBytesField(stream, 17, EncodeDataSet(metric.DataSetValue ?? value as SparkplugDataSet ?? new SparkplugDataSet()));
                break;
            case SparkplugDataType.Template:
                WriteBytesField(stream, 18, EncodeTemplate(metric.TemplateValue ?? value as SparkplugTemplate ?? new SparkplugTemplate()));
                break;
            case SparkplugDataType.PropertySet:
                WriteBytesField(stream, 19, EncodePropertySet(value as SparkplugPropertySet ?? new SparkplugPropertySet()));
                break;
            case SparkplugDataType.PropertySetList:
                WriteBytesField(stream, 20, EncodePropertySetList(value as SparkplugPropertySetList ?? new SparkplugPropertySetList()));
                break;
            default:
                if (IsArrayDataType(metric.DataType))
                    WriteBytesField(stream, 17, EncodeArrayDataSet(metric.DataType, value));
                else
                    WriteStringField(stream, 15, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                break;
        }
    }

    private static byte[] EncodePropertySet(IDictionary<string, string> properties)
    {
        using MemoryStream stream = new MemoryStream();
        foreach (KeyValuePair<string, string> property in properties)
            WriteStringField(stream, 1, property.Key ?? string.Empty);
        foreach (KeyValuePair<string, string> property in properties)
            WriteBytesField(stream, 2, EncodeStringPropertyValue(property.Value));
        return stream.ToArray();
    }

    private static byte[] EncodeStringPropertyValue(string? value)
    {
        using MemoryStream stream = new MemoryStream();
        WriteUInt64Field(stream, 1, (uint)SparkplugDataType.String);
        if (value == null)
            WriteBoolField(stream, 2, true);
        else
            WriteStringField(stream, 8, value);
        return stream.ToArray();
    }

    private static ulong ToUnixMilliseconds(DateTimeOffset timestamp)
    {
        if (timestamp == DateTimeOffset.MinValue)
            timestamp = DateTimeOffset.UtcNow;
        return (ulong)timestamp.ToUniversalTime().ToUnixTimeMilliseconds();
    }

    private static bool ToBoolean(object? value)
    {
        if (value is bool boolean)
            return boolean;
        string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static int ToInt32(object? value)
    {
        try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static uint ToUInt32(object? value)
    {
        try { return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0U; }
    }

    private static long ToInt64(object? value)
    {
        try { return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0L; }
    }

    private static ulong ToUInt64(object? value)
    {
        try { return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0UL; }
    }

    private static float ToSingle(object? value)
    {
        try { return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0F; }
    }

    private static double ToDouble(object? value)
    {
        try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0D; }
    }

    private static void WriteStringField(Stream stream, int fieldNumber, string value)
    {
        WriteBytesField(stream, fieldNumber, Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static void WriteBytesField(Stream stream, int fieldNumber, byte[] value)
    {
        WriteTag(stream, fieldNumber, 2);
        WriteVarint(stream, (ulong)(value == null ? 0 : value.Length));
        if (value != null && value.Length > 0)
            stream.Write(value, 0, value.Length);
    }

    private static void WriteBoolField(Stream stream, int fieldNumber, bool value)
    {
        WriteUInt64Field(stream, fieldNumber, value ? 1UL : 0UL);
    }

    private static void WriteUInt64Field(Stream stream, int fieldNumber, ulong value)
    {
        WriteTag(stream, fieldNumber, 0);
        WriteVarint(stream, value);
    }

    private static void WriteFloatField(Stream stream, int fieldNumber, float value)
    {
        WriteTag(stream, fieldNumber, 5);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteDoubleField(Stream stream, int fieldNumber, double value)
    {
        WriteTag(stream, fieldNumber, 1);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteTag(Stream stream, int fieldNumber, int wireType)
    {
        WriteVarint(stream, (ulong)((fieldNumber << 3) | wireType));
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}
