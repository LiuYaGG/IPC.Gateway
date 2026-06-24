/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OpcUa
* 项目描述 ：
* 类 名 称 ：OpcUaClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OpcUa
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
using System;
using System.Collections;
using System.Globalization;
using IPC.Plc.Communication.Core;
using Opc.UaFx;
using Opc.UaFx.Client;

namespace IPC.Plc.Communication.OpcUa
{
    
    
    
    
    
    
    
    
    
    public sealed class OpcUaClient : IPlcClient
    {
        private readonly PlcConnectionOptions _options;
        private OpcClient _client;
        private bool _connected;

        public OpcUaClient(PlcConnectionOptions options)
        {
            _options = options ?? new PlcConnectionOptions();
        }

        public bool IsConnected
        {
            get { return _connected && _client != null && _client.State == OpcClientState.Connected; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.OpcUa; }
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            _client = new OpcClient(BuildEndpoint());
            _client.OperationTimeout = Math.Max(1000, _options.TimeoutMilliseconds);

            if (!string.IsNullOrWhiteSpace(_options.Username))
                _client.Security.UserIdentity = new OpcClientIdentity(_options.Username, _options.Password ?? string.Empty);

            _client.Connect();
            _connected = true;
        }

        public void Disconnect()
        {
            if (_client != null)
            {
                try
                {
                    _client.Disconnect();
                }
                catch
                {
                }
            }

            _connected = false;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            string nodeId = NormalizeNodeId(address, elementOffset);
            OpcValue value = _client.ReadNode(nodeId);
            object converted = ConvertForRead(value == null ? null : value.Value, dataType, elementCount);
            return new PlcReadResult(0, dataType.ToString(), converted);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            string nodeId = NormalizeNodeId(address, elementOffset);
            object value = ParseValue(dataType, valueText);
            OpcStatus status = _client.WriteNode(nodeId, value);
            if (status != null && !status.IsGood)
                throw new PlcCommunicationException("OPC UA write failed: " + status.Description);
        }

        public void Dispose()
        {
            Disconnect();
            if (_client != null)
                _client.Dispose();
            _client = null;
        }

        private string BuildEndpoint()
        {
            string host = string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host.Trim();
            if (host.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return host;

            int port = _options.Port <= 0 ? 4840 : _options.Port;
            return "opc.tcp://" + host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeNodeId(string address, int elementOffset)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("OPC UA NodeId cannot be empty.", "address");

            string nodeId = address.Trim();
            if (elementOffset > 0 && nodeId.IndexOf("[", StringComparison.Ordinal) < 0)
                nodeId += "[" + elementOffset.ToString(CultureInfo.InvariantCulture) + "]";
            return nodeId;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("OPC UA client is not connected.");
        }

        private static object ConvertForRead(object value, PlcDataType dataType, int elementCount)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
                return ConvertToArray(value, dataType, Math.Max(1, elementCount));
            return ConvertScalar(value, dataType);
        }

        private static Array ConvertToArray(object value, PlcDataType dataType, int count)
        {
            Array result = PlcDataTypeHelper.CreateArray(dataType, count);
            IList list = value as IList;
            for (int i = 0; i < count; i++)
            {
                object item = list != null && i < list.Count ? list[i] : value;
                result.SetValue(ConvertScalar(item, ArrayElementType(dataType)), i);
            }
            return result;
        }

        private static object ParseValue(PlcDataType dataType, string valueText)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
            {
                string[] parts = string.IsNullOrWhiteSpace(valueText)
                    ? new[] { string.Empty }
                    : valueText.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Array values = PlcDataTypeHelper.CreateArray(dataType, Math.Max(1, parts.Length));
                PlcDataType elementType = ArrayElementType(dataType);
                for (int i = 0; i < values.Length; i++)
                    values.SetValue(ConvertScalar(parts[i], elementType), i);
                return values;
            }

            return ConvertScalar(valueText, dataType);
        }

        private static PlcDataType ArrayElementType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                    return PlcDataType.Bool;
                case PlcDataType.Int16Array:
                    return PlcDataType.Int16;
                case PlcDataType.UInt16Array:
                    return PlcDataType.UInt16;
                case PlcDataType.Int32Array:
                    return PlcDataType.Int32;
                case PlcDataType.UInt32Array:
                    return PlcDataType.UInt32;
                case PlcDataType.Int64Array:
                    return PlcDataType.Int64;
                case PlcDataType.UInt64Array:
                    return PlcDataType.UInt64;
                case PlcDataType.FloatArray:
                    return PlcDataType.Float;
                case PlcDataType.DoubleArray:
                    return PlcDataType.Double;
                case PlcDataType.CoilArray:
                    return PlcDataType.Coil;
                case PlcDataType.DiscreteInputArray:
                    return PlcDataType.DiscreteInput;
                default:
                    return dataType;
            }
        }

        private static object ConvertScalar(object value, PlcDataType dataType)
        {
            if (value == null)
                value = string.Empty;

            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    return ParseBool(value);
                case PlcDataType.Int16:
                    return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt16:
                    return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                case PlcDataType.Int32:
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt32:
                    return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case PlcDataType.Int64:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt64:
                    return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                case PlcDataType.Float:
                    return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case PlcDataType.Double:
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case PlcDataType.String:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        private static bool ParseBool(object value)
        {
            if (value is bool)
                return (bool)value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
                return false;

            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
    }
}
