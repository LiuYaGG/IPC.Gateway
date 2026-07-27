using System.Globalization;

namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示脚本可读取的点位快照，并提供常用类型转换方法。
/// </summary>
public sealed class ScriptTagValue
{
    public string Path { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 将当前点位值转换为双精度数字。
    /// </summary>
    public double AsDouble(double fallback = 0D)
    {
        if (Value is IConvertible convertible)
        {
            try
            {
                return convertible.ToDouble(CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        return double.TryParse(ValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    }

    /// <summary>
    /// 将当前点位值转换为三十二位整数。
    /// </summary>
    public int AsInt32(int fallback = 0)
    {
        if (Value is IConvertible convertible)
        {
            try
            {
                return convertible.ToInt32(CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        return int.TryParse(ValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }

    /// <summary>
    /// 将当前点位值转换为布尔值。
    /// </summary>
    public bool AsBoolean(bool fallback = false)
    {
        if (Value is bool boolean)
            return boolean;
        if (bool.TryParse(ValueText, out bool parsedBoolean))
            return parsedBoolean;
        if (double.TryParse(ValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedNumber))
            return Math.Abs(parsedNumber) > double.Epsilon;
        return fallback;
    }

    /// <summary>
    /// 将当前点位值转换为字符串。
    /// </summary>
    public string AsString(string fallback = "")
    {
        return Value?.ToString() ?? (string.IsNullOrEmpty(ValueText) ? fallback : ValueText);
    }
}
