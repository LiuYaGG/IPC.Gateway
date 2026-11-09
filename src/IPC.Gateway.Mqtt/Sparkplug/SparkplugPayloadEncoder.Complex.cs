using System.Collections;

namespace IPC.Gateway.Mqtt.Sparkplug;

public static partial class SparkplugPayloadEncoder
{
    private static byte[] EncodeMetaData(SparkplugMetaData metadata)
    {
        using MemoryStream stream = new();
        if (metadata.IsMultiPart) WriteBoolField(stream, 1, true);
        if (metadata.ContentType.Length > 0) WriteStringField(stream, 2, metadata.ContentType);
        if (metadata.Size > 0) WriteUInt64Field(stream, 3, metadata.Size);
        if (metadata.SequenceNumber > 0) WriteUInt64Field(stream, 4, metadata.SequenceNumber);
        if (metadata.FileName.Length > 0) WriteStringField(stream, 5, metadata.FileName);
        if (metadata.FileType.Length > 0) WriteStringField(stream, 6, metadata.FileType);
        if (metadata.Md5.Length > 0) WriteStringField(stream, 7, metadata.Md5);
        if (metadata.Description.Length > 0) WriteStringField(stream, 8, metadata.Description);
        return stream.ToArray();
    }

    private static byte[] EncodeProperties(SparkplugMetric metric)
    {
        SparkplugPropertySet merged = new();
        foreach (KeyValuePair<string, string> property in metric.Properties)
            merged.Values[property.Key] = SparkplugPropertyValue.String(property.Value);
        foreach (KeyValuePair<string, SparkplugPropertyValue> property in metric.TypedProperties.Values)
            merged.Values[property.Key] = property.Value;
        return EncodePropertySet(merged);
    }

    private static byte[] EncodePropertySet(SparkplugPropertySet propertySet)
    {
        using MemoryStream stream = new();
        foreach (string key in propertySet.Values.Keys)
            WriteStringField(stream, 1, key ?? string.Empty);
        foreach (SparkplugPropertyValue value in propertySet.Values.Values)
            WriteBytesField(stream, 2, EncodePropertyValue(value));
        return stream.ToArray();
    }

    private static byte[] EncodePropertySetList(SparkplugPropertySetList list)
    {
        using MemoryStream stream = new();
        foreach (SparkplugPropertySet propertySet in list.Values)
            WriteBytesField(stream, 1, EncodePropertySet(propertySet));
        return stream.ToArray();
    }

    private static byte[] EncodePropertyValue(SparkplugPropertyValue value)
    {
        value ??= new SparkplugPropertyValue { IsNull = true };
        using MemoryStream stream = new();
        WriteUInt64Field(stream, 1, (uint)value.DataType);
        if (value.IsNull)
        {
            WriteBoolField(stream, 2, true);
            return stream.ToArray();
        }

        WriteScalarValue(stream, value.DataType, value.Value, 3);
        return stream.ToArray();
    }

    private static byte[] EncodeDataSet(SparkplugDataSet dataSet)
    {
        using MemoryStream stream = new();
        uint numberOfColumns = dataSet.NumberOfColumns > 0
            ? dataSet.NumberOfColumns
            : (uint)Math.Max(dataSet.Columns.Count, dataSet.Types.Count);
        WriteUInt64Field(stream, 1, numberOfColumns);
        foreach (string column in dataSet.Columns)
            WriteStringField(stream, 2, column ?? string.Empty);
        foreach (SparkplugDataType type in dataSet.Types)
            WriteUInt64Field(stream, 3, (uint)type);
        foreach (SparkplugDataSetRow row in dataSet.Rows)
            WriteBytesField(stream, 4, EncodeDataSetRow(row, dataSet.Types));
        return stream.ToArray();
    }

    private static byte[] EncodeDataSetRow(SparkplugDataSetRow row, IList<SparkplugDataType> types)
    {
        using MemoryStream stream = new();
        for (int i = 0; i < row.Values.Count; i++)
        {
            SparkplugDataType type = i < types.Count ? types[i] : InferDataType(row.Values[i]);
            WriteBytesField(stream, 1, EncodeDataSetValue(type, row.Values[i]));
        }
        return stream.ToArray();
    }

    private static byte[] EncodeDataSetValue(SparkplugDataType type, object? value)
    {
        using MemoryStream stream = new();
        WriteScalarValue(stream, type, value, 1);
        return stream.ToArray();
    }

    private static byte[] EncodeTemplate(SparkplugTemplate template)
    {
        using MemoryStream stream = new();
        if (template.Version.Length > 0) WriteStringField(stream, 1, template.Version);
        foreach (SparkplugMetric metric in template.Metrics)
            WriteBytesField(stream, 2, EncodeMetric(metric));
        foreach (SparkplugTemplateParameter parameter in template.Parameters)
            WriteBytesField(stream, 3, EncodeTemplateParameter(parameter));
        if (template.TemplateReference.Length > 0) WriteStringField(stream, 4, template.TemplateReference);
        if (template.IsDefinition) WriteBoolField(stream, 5, true);
        return stream.ToArray();
    }

    private static byte[] EncodeTemplateParameter(SparkplugTemplateParameter parameter)
    {
        using MemoryStream stream = new();
        WriteStringField(stream, 1, parameter.Name ?? string.Empty);
        WriteUInt64Field(stream, 2, (uint)parameter.DataType);
        WriteScalarValue(stream, parameter.DataType, parameter.Value, 3);
        return stream.ToArray();
    }

    private static byte[] EncodeArrayDataSet(SparkplugDataType arrayType, object? value)
    {
        SparkplugDataSet dataSet = new() { NumberOfColumns = 1 };
        dataSet.Columns.Add("value");
        SparkplugDataType elementType = GetArrayElementType(arrayType);
        dataSet.Types.Add(elementType);
        if (value is IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                SparkplugDataSetRow row = new();
                row.Values.Add(item);
                dataSet.Rows.Add(row);
            }
        }
        return EncodeDataSet(dataSet);
    }

    private static void WriteScalarValue(Stream stream, SparkplugDataType type, object? value, int firstField)
    {
        switch (type)
        {
            case SparkplugDataType.Int8:
            case SparkplugDataType.Int16:
            case SparkplugDataType.Int32:
                WriteUInt64Field(stream, firstField, unchecked((uint)ToInt32(value)));
                break;
            case SparkplugDataType.UInt8:
            case SparkplugDataType.UInt16:
            case SparkplugDataType.UInt32:
                WriteUInt64Field(stream, firstField, ToUInt32(value));
                break;
            case SparkplugDataType.Int64:
                WriteUInt64Field(stream, firstField + 1, unchecked((ulong)ToInt64(value)));
                break;
            case SparkplugDataType.UInt64:
            case SparkplugDataType.DateTime:
                WriteUInt64Field(stream, firstField + 1, ToUInt64(value));
                break;
            case SparkplugDataType.Float:
                WriteFloatField(stream, firstField + 2, ToSingle(value));
                break;
            case SparkplugDataType.Double:
                WriteDoubleField(stream, firstField + 3, ToDouble(value));
                break;
            case SparkplugDataType.Boolean:
                WriteBoolField(stream, firstField + 4, ToBoolean(value));
                break;
            case SparkplugDataType.PropertySet:
                WriteBytesField(stream, firstField + 6, EncodePropertySet(value as SparkplugPropertySet ?? new SparkplugPropertySet()));
                break;
            case SparkplugDataType.PropertySetList:
                WriteBytesField(stream, firstField + 7, EncodePropertySetList(value as SparkplugPropertySetList ?? new SparkplugPropertySetList()));
                break;
            default:
                WriteStringField(stream, firstField + 5, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                break;
        }
    }

    private static bool IsArrayDataType(SparkplugDataType type) => type is >= SparkplugDataType.Int8Array and <= SparkplugDataType.DateTimeArray;

    private static SparkplugDataType GetArrayElementType(SparkplugDataType type) => type switch
    {
        SparkplugDataType.Int8Array => SparkplugDataType.Int8,
        SparkplugDataType.Int16Array => SparkplugDataType.Int16,
        SparkplugDataType.Int32Array => SparkplugDataType.Int32,
        SparkplugDataType.Int64Array => SparkplugDataType.Int64,
        SparkplugDataType.UInt8Array => SparkplugDataType.UInt8,
        SparkplugDataType.UInt16Array => SparkplugDataType.UInt16,
        SparkplugDataType.UInt32Array => SparkplugDataType.UInt32,
        SparkplugDataType.UInt64Array => SparkplugDataType.UInt64,
        SparkplugDataType.FloatArray => SparkplugDataType.Float,
        SparkplugDataType.DoubleArray => SparkplugDataType.Double,
        SparkplugDataType.BooleanArray => SparkplugDataType.Boolean,
        SparkplugDataType.StringArray => SparkplugDataType.String,
        SparkplugDataType.DateTimeArray => SparkplugDataType.DateTime,
        _ => SparkplugDataType.Unknown
    };

    private static SparkplugDataType InferDataType(object? value) => value switch
    {
        bool => SparkplugDataType.Boolean,
        float => SparkplugDataType.Float,
        double or decimal => SparkplugDataType.Double,
        sbyte => SparkplugDataType.Int8,
        short => SparkplugDataType.Int16,
        int => SparkplugDataType.Int32,
        long => SparkplugDataType.Int64,
        byte => SparkplugDataType.UInt8,
        ushort => SparkplugDataType.UInt16,
        uint => SparkplugDataType.UInt32,
        ulong => SparkplugDataType.UInt64,
        _ => SparkplugDataType.String
    };
}
