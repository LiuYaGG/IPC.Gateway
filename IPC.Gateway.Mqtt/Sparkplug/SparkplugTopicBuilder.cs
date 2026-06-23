/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Mqtt.Sparkplug
* 项目描述 ：
* 类 名 称 ：SparkplugTopicBuilder
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
namespace IPC.Gateway.Mqtt.Sparkplug;

public sealed class SparkplugTopicBuilder
{
    public SparkplugTopicBuilder(string namespaceName, string groupId, string edgeNodeId)
    {
        Namespace = Normalize(namespaceName, "spBv1.0");
        GroupId = Normalize(groupId, "IPC-Gateway");
        EdgeNodeId = Normalize(edgeNodeId, "EdgeNode");
    }

    public string Namespace { get; }
    public string GroupId { get; }
    public string EdgeNodeId { get; }

    public string NodeBirth()
    {
        return Build("NBIRTH", string.Empty);
    }

    public string NodeData()
    {
        return Build("NDATA", string.Empty);
    }

    public string NodeDeath()
    {
        return Build("NDEATH", string.Empty);
    }

    public string DeviceBirth(string deviceId)
    {
        return Build("DBIRTH", deviceId);
    }

    public string DeviceData(string deviceId)
    {
        return Build("DDATA", deviceId);
    }

    public string DeviceDeath(string deviceId)
    {
        return Build("DDEATH", deviceId);
    }

    private string Build(string messageType, string deviceId)
    {
        string topic = Namespace + "/" + GroupId + "/" + Normalize(messageType, "NDATA") + "/" + EdgeNodeId;
        if (!string.IsNullOrWhiteSpace(deviceId))
            topic += "/" + Normalize(deviceId, "Device");
        return topic.Trim('/');
    }

    public static string Normalize(string value, string fallback)
    {
        string text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        text = text.Replace('\\', '/').Replace('+', '_').Replace('#', '_');
        while (text.Contains("//", StringComparison.Ordinal))
            text = text.Replace("//", "/", StringComparison.Ordinal);
        text = text.Trim('/');
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
