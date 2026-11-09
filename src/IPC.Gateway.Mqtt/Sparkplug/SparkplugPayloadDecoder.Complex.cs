using System.Text;

namespace IPC.Gateway.Mqtt.Sparkplug;

public static partial class SparkplugPayloadDecoder
{
    private static SparkplugMetaData DecodeMetaData(ReadOnlySpan<byte> bytes)
    {
        SparkplugMetaData metadata = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 0: metadata.IsMultiPart = ReadVarint(bytes, ref offset) != 0; break;
                case 2 when wire == 2: metadata.ContentType = ReadString(bytes, ref offset); break;
                case 3 when wire == 0: metadata.Size = ReadVarint(bytes, ref offset); break;
                case 4 when wire == 0: metadata.SequenceNumber = ReadVarint(bytes, ref offset); break;
                case 5 when wire == 2: metadata.FileName = ReadString(bytes, ref offset); break;
                case 6 when wire == 2: metadata.FileType = ReadString(bytes, ref offset); break;
                case 7 when wire == 2: metadata.Md5 = ReadString(bytes, ref offset); break;
                case 8 when wire == 2: metadata.Description = ReadString(bytes, ref offset); break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        return metadata;
    }

    private static SparkplugPropertySet DecodePropertySet(ReadOnlySpan<byte> bytes)
    {
        List<string> keys = new();
        List<SparkplugPropertyValue> values = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
                keys.Add(ReadString(bytes, ref offset));
            else if (field == 2 && wire == 2)
                values.Add(DecodePropertyValue(ReadLengthDelimited(bytes, ref offset)));
            else
                Skip(bytes, ref offset, wire);
        }

        SparkplugPropertySet propertySet = new();
        for (int i = 0; i < Math.Max(keys.Count, values.Count); i++)
        {
            string key = i < keys.Count && keys[i].Length > 0 ? keys[i] : "property" + i;
            propertySet.Values[key] = i < values.Count ? values[i] : new SparkplugPropertyValue { IsNull = true };
        }
        return propertySet;
    }

    private static SparkplugPropertySetList DecodePropertySetList(ReadOnlySpan<byte> bytes)
    {
        SparkplugPropertySetList list = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
                list.Values.Add(DecodePropertySet(ReadLengthDelimited(bytes, ref offset)));
            else
                Skip(bytes, ref offset, wire);
        }
        return list;
    }

    private static SparkplugPropertyValue DecodePropertyValue(ReadOnlySpan<byte> bytes)
    {
        SparkplugPropertyValue value = new();
        object? raw = null;
        int rawField = 0;
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 0: value.DataType = (SparkplugDataType)(uint)ReadVarint(bytes, ref offset); break;
                case 2 when wire == 0: value.IsNull = ReadVarint(bytes, ref offset) != 0; break;
                case 3 when wire == 0: raw = ReadVarint(bytes, ref offset); rawField = 3; break;
                case 4 when wire == 0: raw = ReadVarint(bytes, ref offset); rawField = 4; break;
                case 5 when wire == 5: raw = ReadSingle(bytes, ref offset); rawField = 5; break;
                case 6 when wire == 1: raw = ReadDouble(bytes, ref offset); rawField = 6; break;
                case 7 when wire == 0: raw = ReadVarint(bytes, ref offset) != 0; rawField = 7; break;
                case 8 when wire == 2: raw = ReadString(bytes, ref offset); rawField = 8; break;
                case 9 when wire == 2: raw = DecodePropertySet(ReadLengthDelimited(bytes, ref offset)); rawField = 9; break;
                case 10 when wire == 2: raw = DecodePropertySetList(ReadLengthDelimited(bytes, ref offset)); rawField = 10; break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        value.Value = value.IsNull ? null : ConvertPropertyValue(value.DataType, raw, rawField);
        return value;
    }

    private static object? ConvertPropertyValue(SparkplugDataType type, object? raw, int rawField)
    {
        if (raw == null)
            return null;
        if (rawField == 3 && raw is ulong intValue)
            return DecodeIntValue(type, intValue);
        if (rawField == 4 && raw is ulong longValue)
            return DecodeLongValue(type, longValue);
        return raw;
    }

    private static void CopyProperties(SparkplugPropertySet properties, SparkplugMetric metric)
    {
        foreach (KeyValuePair<string, SparkplugPropertyValue> property in properties.Values)
        {
            metric.TypedProperties.Values[property.Key] = property.Value;
            if (!property.Value.IsNull && property.Value.DataType is SparkplugDataType.String or SparkplugDataType.Text)
                metric.Properties[property.Key] = Convert.ToString(property.Value.Value) ?? string.Empty;
        }
    }

    private static SparkplugDataSet DecodeDataSet(ReadOnlySpan<byte> bytes)
    {
        SparkplugDataSet dataSet = new();
        List<byte[]> encodedRows = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 0: dataSet.NumberOfColumns = checked((uint)ReadVarint(bytes, ref offset)); break;
                case 2 when wire == 2: dataSet.Columns.Add(ReadString(bytes, ref offset)); break;
                case 3 when wire == 0: dataSet.Types.Add((SparkplugDataType)(uint)ReadVarint(bytes, ref offset)); break;
                case 4 when wire == 2: encodedRows.Add(ReadLengthDelimited(bytes, ref offset).ToArray()); break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        foreach (byte[] row in encodedRows)
            dataSet.Rows.Add(DecodeDataSetRow(row, dataSet.Types));
        return dataSet;
    }

    private static SparkplugDataSetRow DecodeDataSetRow(ReadOnlySpan<byte> bytes, IList<SparkplugDataType> types)
    {
        SparkplugDataSetRow row = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                SparkplugDataType type = row.Values.Count < types.Count ? types[row.Values.Count] : SparkplugDataType.Unknown;
                row.Values.Add(DecodeDataSetValue(ReadLengthDelimited(bytes, ref offset), type));
            }
            else
                Skip(bytes, ref offset, wire);
        }
        return row;
    }

    private static object? DecodeDataSetValue(ReadOnlySpan<byte> bytes, SparkplugDataType type)
    {
        object? value = null;
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 0: value = DecodeIntValue(type, ReadVarint(bytes, ref offset)); break;
                case 2 when wire == 0: value = DecodeLongValue(type, ReadVarint(bytes, ref offset)); break;
                case 3 when wire == 5: value = ReadSingle(bytes, ref offset); break;
                case 4 when wire == 1: value = ReadDouble(bytes, ref offset); break;
                case 5 when wire == 0: value = ReadVarint(bytes, ref offset) != 0; break;
                case 6 when wire == 2: value = ReadString(bytes, ref offset); break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        return value;
    }

    private static SparkplugTemplate DecodeTemplate(ReadOnlySpan<byte> bytes)
    {
        SparkplugTemplate template = new();
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 2: template.Version = ReadString(bytes, ref offset); break;
                case 2 when wire == 2: template.Metrics.Add(DecodeMetric(ReadLengthDelimited(bytes, ref offset))); break;
                case 3 when wire == 2: template.Parameters.Add(DecodeTemplateParameter(ReadLengthDelimited(bytes, ref offset))); break;
                case 4 when wire == 2: template.TemplateReference = ReadString(bytes, ref offset); break;
                case 5 when wire == 0: template.IsDefinition = ReadVarint(bytes, ref offset) != 0; break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        return template;
    }

    private static SparkplugTemplateParameter DecodeTemplateParameter(ReadOnlySpan<byte> bytes)
    {
        SparkplugTemplateParameter parameter = new();
        object? raw = null;
        int rawField = 0;
        int offset = 0;
        while (offset < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref offset);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            switch (field)
            {
                case 1 when wire == 2: parameter.Name = ReadString(bytes, ref offset); break;
                case 2 when wire == 0: parameter.DataType = (SparkplugDataType)(uint)ReadVarint(bytes, ref offset); break;
                case 3 when wire == 0: raw = ReadVarint(bytes, ref offset); rawField = 3; break;
                case 4 when wire == 0: raw = ReadVarint(bytes, ref offset); rawField = 4; break;
                case 5 when wire == 5: raw = ReadSingle(bytes, ref offset); rawField = 5; break;
                case 6 when wire == 1: raw = ReadDouble(bytes, ref offset); rawField = 6; break;
                case 7 when wire == 0: raw = ReadVarint(bytes, ref offset) != 0; rawField = 7; break;
                case 8 when wire == 2: raw = ReadString(bytes, ref offset); rawField = 8; break;
                default: Skip(bytes, ref offset, wire); break;
            }
        }
        parameter.Value = ConvertPropertyValue(parameter.DataType, raw, rawField);
        return parameter;
    }

    private static object?[] DecodeArrayValue(SparkplugDataSet dataSet) =>
        dataSet.Rows.Select(row => row.Values.Count > 0 ? row.Values[0] : null).ToArray();

    private static bool IsArrayDataType(SparkplugDataType type) =>
        type is >= SparkplugDataType.Int8Array and <= SparkplugDataType.DateTimeArray;

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset) =>
        Encoding.UTF8.GetString(ReadLengthDelimited(bytes, ref offset));
}
