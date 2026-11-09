namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugDataSet
{
    public uint NumberOfColumns { get; set; }
    public IList<string> Columns { get; } = new List<string>();
    public IList<SparkplugDataType> Types { get; } = new List<SparkplugDataType>();
    public IList<SparkplugDataSetRow> Rows { get; } = new List<SparkplugDataSetRow>();
}

public sealed class SparkplugDataSetRow
{
    public IList<object?> Values { get; } = new List<object?>();
}
