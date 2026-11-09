namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugTemplate
{
    public string Version { get; set; } = string.Empty;
    public string TemplateReference { get; set; } = string.Empty;
    public bool IsDefinition { get; set; }
    public IList<SparkplugMetric> Metrics { get; } = new List<SparkplugMetric>();
    public IList<SparkplugTemplateParameter> Parameters { get; } = new List<SparkplugTemplateParameter>();
}

public sealed class SparkplugTemplateParameter
{
    public string Name { get; set; } = string.Empty;
    public SparkplugDataType DataType { get; set; }
    public object? Value { get; set; }
}
