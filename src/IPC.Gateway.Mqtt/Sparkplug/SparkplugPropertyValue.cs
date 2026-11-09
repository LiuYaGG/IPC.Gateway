namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugPropertyValue
{
    public SparkplugDataType DataType { get; set; } = SparkplugDataType.String;
    public bool IsNull { get; set; }
    public object? Value { get; set; }

    public static SparkplugPropertyValue String(string? value) => new()
    {
        DataType = SparkplugDataType.String,
        IsNull = value == null,
        Value = value
    };
}

public sealed class SparkplugPropertySet
{
    public IDictionary<string, SparkplugPropertyValue> Values { get; } =
        new Dictionary<string, SparkplugPropertyValue>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SparkplugPropertySetList
{
    public IList<SparkplugPropertySet> Values { get; } = new List<SparkplugPropertySet>();
}
