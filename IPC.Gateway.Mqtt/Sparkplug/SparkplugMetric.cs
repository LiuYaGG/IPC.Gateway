/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Mqtt.Sparkplug
* 项目描述 ：
* 类 名 称 ：SparkplugMetric
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
using System.Globalization;

namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugMetric
{
    public SparkplugMetric()
    {
        Name = string.Empty;
        DataType = SparkplugDataType.String;
        Timestamp = DateTimeOffset.UtcNow;
        Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; set; }
    public ulong? Alias { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public SparkplugDataType DataType { get; set; }
    public bool IsNull { get; set; }
    public object? Value { get; set; }
    public IDictionary<string, string> Properties { get; }

    public static SparkplugMetric String(string name, string value)
    {
        return new SparkplugMetric
        {
            Name = name ?? string.Empty,
            DataType = SparkplugDataType.String,
            Value = value ?? string.Empty
        };
    }

    public static SparkplugMetric Boolean(string name, bool value)
    {
        return new SparkplugMetric
        {
            Name = name ?? string.Empty,
            DataType = SparkplugDataType.Boolean,
            Value = value
        };
    }

    public static SparkplugMetric Int64(string name, long value)
    {
        return new SparkplugMetric
        {
            Name = name ?? string.Empty,
            DataType = SparkplugDataType.Int64,
            Value = value
        };
    }

    public static SparkplugMetric UInt64(string name, ulong value)
    {
        return new SparkplugMetric
        {
            Name = name ?? string.Empty,
            DataType = SparkplugDataType.UInt64,
            Value = value
        };
    }

    public static SparkplugMetric FromText(string name, string dataType, string valueText)
    {
        string type = string.IsNullOrWhiteSpace(dataType) ? string.Empty : dataType.Trim();
        string value = valueText ?? string.Empty;
        SparkplugMetric metric = new SparkplugMetric { Name = name ?? string.Empty };

        if (type.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Coil", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("DiscreteInput", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Boolean;
            metric.Value = value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("on", StringComparison.OrdinalIgnoreCase);
            return metric;
        }

        if (type.Equals("Float", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Float;
            metric.Value = ParseSingle(value);
            return metric;
        }

        if (type.Equals("Double", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Double;
            metric.Value = ParseDouble(value);
            return metric;
        }

        if (type.Equals("Int16", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Int16;
            metric.Value = ParseInt32(value);
            return metric;
        }

        if (type.Equals("UInt16", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.UInt16;
            metric.Value = ParseUInt32(value);
            return metric;
        }

        if (type.Equals("Int32", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Int32;
            metric.Value = ParseInt32(value);
            return metric;
        }

        if (type.Equals("UInt32", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.UInt32;
            metric.Value = ParseUInt32(value);
            return metric;
        }

        if (type.Equals("Int64", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.Int64;
            metric.Value = ParseInt64(value);
            return metric;
        }

        if (type.Equals("UInt64", StringComparison.OrdinalIgnoreCase))
        {
            metric.DataType = SparkplugDataType.UInt64;
            metric.Value = ParseUInt64(value);
            return metric;
        }

        metric.DataType = SparkplugDataType.String;
        metric.Value = value;
        return metric;
    }

    private static int ParseInt32(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    private static uint ParseUInt32(string value)
    {
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : 0U;
    }

    private static long ParseInt64(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0L;
    }

    private static ulong ParseUInt64(string value)
    {
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
            ? parsed
            : 0UL;
    }

    private static float ParseSingle(string value)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0F;
    }

    private static double ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0D;
    }
}
