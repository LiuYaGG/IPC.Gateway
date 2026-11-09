namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugMetaData
{
    public bool IsMultiPart { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public ulong Size { get; set; }
    public ulong SequenceNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
