/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.VirtualPlc
* 项目描述 ：
* 类 名 称 ：VirtualPlcClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.VirtualPlc
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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.VirtualPlc
{
    
    
    
    
    
    
    
    
    
    public sealed class VirtualPlcClient : IPlcClient, IAsyncPlcClient
    {
        private static readonly object StoreLock = new object();
        private static readonly Dictionary<string, Dictionary<string, object>> Stores = new Dictionary<string, Dictionary<string, object>>();

        private readonly PlcConnectionOptions _options;
        private readonly string _storeKey;
        private bool _connected;

        public VirtualPlcClient(PlcConnectionOptions options)
        {
            _options = options ?? new PlcConnectionOptions();
            _storeKey = string.IsNullOrWhiteSpace(_options.Host) ? "default" : _options.Host.Trim();
        }

        public bool IsConnected
        {
            get { return _connected; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.VirtualPlc; }
        }

        public void Connect()
        {
            lock (StoreLock)
            {
                if (!Stores.ContainsKey(_storeKey))
                    Stores[_storeKey] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            _connected = true;
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Connect();
            return ValueTask.CompletedTask;
        }

        public void Disconnect()
        {
            _connected = false;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            string key = NormalizeAddress(address);
            object? storedValue = null;

            lock (StoreLock)
            {
                Dictionary<string, object> store = Stores[_storeKey];
                store.TryGetValue(key, out storedValue);
            }

            object value = ConvertForRead(storedValue, dataType, elementCount);
            return new PlcReadResult(0, dataType.ToString(), value);
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PlcReadResult>(Read(address, dataType, elementCount, elementOffset));
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            string key = NormalizeAddress(address);
            object value = ParseValue(dataType, valueText);

            lock (StoreLock)
            {
                Stores[_storeKey][key] = value;
            }
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(address, dataType, valueText, elementOffset);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void EnsureConnected()
        {
            if (!_connected)
                throw new InvalidOperationException("Virtual PLC is not connected.");
        }

        private static string NormalizeAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Address cannot be empty.", "address");
            return address.Trim();
        }

        private static object ConvertForRead(object? storedValue, PlcDataType dataType, int elementCount)
        {
            if (storedValue == null)
                return CreateDefaultValue(dataType, elementCount);

            if (PlcDataTypeHelper.IsArray(dataType))
                return ConvertToArray(storedValue, dataType, Math.Max(1, elementCount));

            return ConvertScalar(storedValue, dataType);
        }

        private static object CreateDefaultValue(PlcDataType dataType, int elementCount)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
                return PlcDataTypeHelper.CreateArray(dataType, Math.Max(1, elementCount));

            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    return false;
                case PlcDataType.Int16:
                    return (short)0;
                case PlcDataType.UInt16:
                    return (ushort)0;
                case PlcDataType.Int32:
                    return 0;
                case PlcDataType.UInt32:
                    return (uint)0;
                case PlcDataType.Int64:
                    return (long)0;
                case PlcDataType.UInt64:
                    return (ulong)0;
                case PlcDataType.Float:
                    return 0F;
                case PlcDataType.Double:
                    return 0D;
                case PlcDataType.String:
                    return string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static Array ConvertToArray(object storedValue, PlcDataType dataType, int count)
        {
            Array result = PlcDataTypeHelper.CreateArray(dataType, count);
            List<object> values = ToObjectList(storedValue);

            for (int i = 0; i < count; i++)
            {
                object? item = i < values.Count ? values[i] : null;
                result.SetValue(ConvertScalar(item, ArrayElementType(dataType)), i);
            }

            return result;
        }

        private static List<object> ToObjectList(object? value)
        {
            List<object> values = new List<object>();
            if (value == null)
                return values;

            if (value is string text)
            {
                values.Add(text);
                return values;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                    values.Add(item ?? string.Empty);
                return values;
            }

            values.Add(value);
            return values;
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

        private static object ParseValue(PlcDataType dataType, string valueText)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
            {
                string[] parts = SplitValues(valueText);
                Array values = PlcDataTypeHelper.CreateArray(dataType, Math.Max(1, parts.Length));
                PlcDataType elementType = ArrayElementType(dataType);
                for (int i = 0; i < values.Length; i++)
                    values.SetValue(ConvertScalar(parts[i], elementType), i);
                return values;
            }

            return ConvertScalar(valueText, dataType);
        }

        private static string[] SplitValues(string valueText)
        {
            if (string.IsNullOrWhiteSpace(valueText))
                return new[] { string.Empty };

            return valueText.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static object ConvertScalar(object? value, PlcDataType dataType)
        {
            if (value == null)
                return CreateDefaultValue(dataType, 1);

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
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        private static bool ParseBool(object value)
        {
            if (value is bool)
                return (bool)value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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
