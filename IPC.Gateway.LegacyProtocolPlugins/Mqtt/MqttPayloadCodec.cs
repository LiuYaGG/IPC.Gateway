using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Mqtt
{
    public static class MqttPayloadCodec
    {
        public static object DecodeText(string payload, string selector, PlcDataType dataType, int elementCount, int elementOffset, bool jsonMode)
        {
            if (!jsonMode)
            {
                if (!string.IsNullOrEmpty(selector))
                    throw new FormatException("Text 模式的 MQTT 标签地址不能包含选择器。");
                return ConvertScalar(payload ?? string.Empty, dataType);
            }

            using JsonDocument document = JsonDocument.Parse(payload ?? string.Empty);
            JsonElement value = Select(document.RootElement, selector);
            if (PlcDataTypeHelper.IsArray(dataType))
                return ConvertArray(value, dataType, elementCount, elementOffset);
            return ConvertJsonScalar(value, dataType);
        }

        public static object ConvertMetricValue(object value, PlcDataType dataType)
        {
            if (value == null)
                throw new MqttTagException("Sparkplug B 指标值为空。");
            if (PlcDataTypeHelper.IsArray(dataType))
                throw new NotSupportedException("Sparkplug B 普通指标不映射为 PLC 数组类型。");
            if (dataType == PlcDataType.String)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (dataType == PlcDataType.Bool)
                return value is bool boolean ? boolean : ParseBoolean(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            return Convert.ChangeType(value, GetScalarType(dataType), CultureInfo.InvariantCulture);
        }

        private static JsonElement Select(JsonElement root, string selector)
        {
            if (string.IsNullOrWhiteSpace(selector) || selector == "$")
                return root;
            string normalized = selector.StartsWith("$.", StringComparison.Ordinal) ? selector.Substring(2) : selector;
            JsonElement current = root;
            foreach (string segment in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    throw new MqttTagException("JSON payload 中不存在字段 " + selector + "。");
            }
            return current;
        }

        private static object ConvertJsonScalar(JsonElement value, PlcDataType dataType)
        {
            if (dataType == PlcDataType.String)
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            if (dataType == PlcDataType.Bool)
                return value.ValueKind == JsonValueKind.True || value.ValueKind != JsonValueKind.False && ParseBoolean(value.ToString());
            return ConvertScalar(value.ToString(), dataType);
        }

        private static object ConvertArray(JsonElement value, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (value.ValueKind != JsonValueKind.Array)
                throw new MqttTagException("JSON 字段不是数组。");
            int available = value.GetArrayLength() - elementOffset;
            if (elementOffset < 0 || available < elementCount || elementCount <= 0)
                throw new MqttTagException("JSON 数组长度不足以满足元素偏移和数量。");
            Array result = PlcDataTypeHelper.CreateArray(dataType, elementCount);
            int sourceIndex = 0;
            int targetIndex = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (sourceIndex++ < elementOffset)
                    continue;
                if (targetIndex >= elementCount)
                    break;
                PlcDataType scalar = ToScalarType(dataType);
                result.SetValue(ConvertJsonScalar(item, scalar), targetIndex++);
            }
            return result;
        }

        private static object ConvertScalar(string text, PlcDataType dataType)
        {
            if (dataType == PlcDataType.String) return text;
            if (dataType == PlcDataType.Bool) return ParseBoolean(text);
            return Convert.ChangeType(text, GetScalarType(dataType), CultureInfo.InvariantCulture);
        }

        private static bool ParseBoolean(string text)
        {
            if (bool.TryParse(text, out bool value)) return value;
            if (text == "1") return true;
            if (text == "0") return false;
            throw new FormatException("布尔值应为 true、false、1 或 0。");
        }

        private static Type GetScalarType(PlcDataType dataType)
        {
            return dataType switch
            {
                PlcDataType.Int8 => typeof(sbyte), PlcDataType.UInt8 => typeof(byte),
                PlcDataType.Int16 => typeof(short), PlcDataType.UInt16 => typeof(ushort),
                PlcDataType.Int32 => typeof(int), PlcDataType.UInt32 => typeof(uint),
                PlcDataType.Int64 => typeof(long), PlcDataType.UInt64 => typeof(ulong),
                PlcDataType.Float => typeof(float), PlcDataType.Double => typeof(double),
                _ => throw new NotSupportedException("MQTT 不支持数据类型 " + dataType + "。")
            };
        }

        private static PlcDataType ToScalarType(PlcDataType dataType)
        {
            return dataType switch
            {
                PlcDataType.BoolArray => PlcDataType.Bool,
                PlcDataType.Int8Array => PlcDataType.Int8,
                PlcDataType.UInt8Array => PlcDataType.UInt8,
                PlcDataType.Int16Array => PlcDataType.Int16,
                PlcDataType.UInt16Array => PlcDataType.UInt16,
                PlcDataType.Int32Array => PlcDataType.Int32,
                PlcDataType.UInt32Array => PlcDataType.UInt32,
                PlcDataType.Int64Array => PlcDataType.Int64,
                PlcDataType.UInt64Array => PlcDataType.UInt64,
                PlcDataType.FloatArray => PlcDataType.Float,
                PlcDataType.DoubleArray => PlcDataType.Double,
                _ => throw new NotSupportedException("MQTT 不支持数据类型 " + dataType + "。")
            };
        }
    }

    internal sealed class MqttTagException : Exception
    {
        public MqttTagException(string message) : base(message) { }
    }
}
