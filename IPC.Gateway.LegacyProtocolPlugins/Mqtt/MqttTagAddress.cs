using System;

namespace IPC.Plc.Communication.Mqtt
{
    public sealed class MqttTagAddress
    {
        private MqttTagAddress(string topic, string selector)
        {
            Topic = topic;
            Selector = selector;
        }

        public string Topic { get; }
        public string Selector { get; }
        public string CacheKey => string.IsNullOrEmpty(Selector) ? Topic : Topic + "|" + Selector;

        public static MqttTagAddress Parse(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            int separator = normalized.IndexOf('|');
            string topic = (separator < 0 ? normalized : normalized.Substring(0, separator)).Trim();
            string selector = separator < 0 ? string.Empty : normalized.Substring(separator + 1).Trim();
            if (topic.Length == 0 || topic.IndexOfAny(new[] { '#', '+' }) >= 0)
                throw new FormatException("MQTT 标签地址必须使用精确主题，不能包含 # 或 +。");
            if (separator >= 0 && selector.Length == 0)
                throw new FormatException("MQTT 地址的选择器不能为空。");
            return new MqttTagAddress(topic, selector);
        }
    }
}
