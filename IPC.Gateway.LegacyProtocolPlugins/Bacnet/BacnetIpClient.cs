using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using IPC.Plc.Communication.Core;
using System.IO.BACnet;

namespace IPC.Plc.Communication.Bacnet
{
    public sealed class BacnetIpClient : IPlcClient, IPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private readonly BacnetDriverOptions _driverOptions;
        private BacnetClient _client;
        private BacnetAddress _address;

        public BacnetIpClient(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _driverOptions = BacnetDriverOptions.Parse(_options.DriverOptionsJson);
        }

        public bool IsConnected
        {
            get { return _client != null; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.BacnetIp; }
        }

        public void Connect()
        {
            if (_client != null)
                return;

            string host = (_options.Host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("BACnet/IP host cannot be empty.");

            int remotePort = _options.Port > 0 ? _options.Port : 47808;
            int timeout = _options.TimeoutMilliseconds > 0 ? _options.TimeoutMilliseconds : 3000;
            string endpoint = host.IndexOf(':') >= 0 ? host : host + ":" + remotePort.ToString(CultureInfo.InvariantCulture);

            BacnetIpUdpProtocolTransport transport = new BacnetIpUdpProtocolTransport(
                _driverOptions.LocalPort,
                _driverOptions.UseExclusivePort,
                _driverOptions.DontFragment,
                _driverOptions.MaxPayload,
                _driverOptions.LocalEndpointIp);

            BacnetClient client = new BacnetClient(transport, TimeSpan.FromMilliseconds(timeout), _driverOptions.Retries);
            client.WritePriority = (byte)_driverOptions.WritePriority;
            client.Start();

            _address = new BacnetAddress(BacnetAddressTypes.IP, endpoint, 0);
            _client = client;
        }

        public void Disconnect()
        {
            BacnetClient client = _client;
            _client = null;
            _address = null;
            if (client != null)
                client.Dispose();
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            BacnetTagAddress tagAddress = BacnetTagAddress.Parse(address);
            IList<BacnetValue> values = tagAddress.HasArrayIndex
                ? new List<BacnetValue> { _client.ReadPropertyRequest(_address, tagAddress.ObjectId, tagAddress.PropertyId, tagAddress.ArrayIndex) }
                : _client.ReadPropertyRequest(_address, tagAddress.ObjectId, tagAddress.PropertyId);

            object value = ConvertReadValues(values, dataType, elementCount);
            return new PlcReadResult(0, dataType.ToString(), value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;

            if (!IsConnected)
                Connect();

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<BacnetBatchItem> items = BuildBatchItems(requests, results);
            List<List<BacnetBatchItem>> chunks = BuildBatchChunks(items, _driverOptions.MaxBatchObjects);

            for (int i = 0; i < chunks.Count; i++)
                ExecuteBatchChunk(chunks[i], results);

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "BACnet batch read did not produce a result.", PlcReadFailureScope.Batch));
            }

            return output;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            BacnetTagAddress tagAddress = BacnetTagAddress.Parse(address);
            BacnetValue value = CreateWriteValue(tagAddress, dataType, valueText);
            _client.WritePropertyRequest(_address, tagAddress.ObjectId, tagAddress.PropertyId, value);
        }

        public void Dispose()
        {
            Disconnect();
        }

        private List<BacnetBatchItem> BuildBatchItems(IList<PlcBatchReadRequest> requests, PlcBatchReadResult[] results)
        {
            List<BacnetBatchItem> items = new List<BacnetBatchItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    if (request.ElementCount <= 0)
                        throw new ArgumentOutOfRangeException("ElementCount");
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    BacnetTagAddress tagAddress = BacnetTagAddress.Parse(request.Address);
                    items.Add(new BacnetBatchItem(i, request, tagAddress));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }

            return items;
        }

        private static List<List<BacnetBatchItem>> BuildBatchChunks(List<BacnetBatchItem> items, int maxObjects)
        {
            List<List<BacnetBatchItem>> chunks = new List<List<BacnetBatchItem>>();
            if (items == null || items.Count == 0)
                return chunks;

            int limit = Math.Max(1, maxObjects);
            List<BacnetBatchItem> current = new List<BacnetBatchItem>();
            HashSet<string> objectKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < items.Count; i++)
            {
                BacnetBatchItem item = items[i];
                bool newObject = !objectKeys.Contains(item.ObjectKey);
                if (current.Count > 0 && newObject && objectKeys.Count >= limit)
                {
                    chunks.Add(current);
                    current = new List<BacnetBatchItem>();
                    objectKeys.Clear();
                }

                current.Add(item);
                objectKeys.Add(item.ObjectKey);
            }

            if (current.Count > 0)
                chunks.Add(current);

            return chunks;
        }

        private void ExecuteBatchChunk(List<BacnetBatchItem> items, PlcBatchReadResult[] results)
        {
            if (items == null || items.Count == 0)
                return;

            try
            {
                IList<BacnetReadAccessSpecification> specifications = BuildReadAccessSpecifications(items);
                IList<BacnetReadAccessResult> response = _client.ReadPropertyMultipleRequest(_address, specifications);
                ApplyBatchResponse(items, response, results);
            }
            catch (Exception ex)
            {
                if (IsCommunicationException(ex))
                {
                    for (int i = 0; i < items.Count; i++)
                        results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, PlcReadFailureScope.Transport);
                    return;
                }

                RetryBatchItemsIndividually(items, results);
            }
        }

        private static IList<BacnetReadAccessSpecification> BuildReadAccessSpecifications(List<BacnetBatchItem> items)
        {
            Dictionary<string, List<BacnetBatchItem>> grouped = new Dictionary<string, List<BacnetBatchItem>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                BacnetBatchItem item = items[i];
                List<BacnetBatchItem> group;
                if (!grouped.TryGetValue(item.ObjectKey, out group))
                {
                    group = new List<BacnetBatchItem>();
                    grouped[item.ObjectKey] = group;
                }
                group.Add(item);
            }

            List<BacnetReadAccessSpecification> specifications = new List<BacnetReadAccessSpecification>();
            foreach (List<BacnetBatchItem> group in grouped.Values)
            {
                BacnetBatchItem first = group[0];
                List<BacnetPropertyReference> properties = new List<BacnetPropertyReference>();
                HashSet<string> propertyKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < group.Count; i++)
                {
                    BacnetBatchItem item = group[i];
                    string key = item.PropertyKey;
                    if (!propertyKeys.Add(key))
                        continue;

                    properties.Add(new BacnetPropertyReference(
                        (uint)item.Address.PropertyId,
                        item.Address.HasArrayIndex ? item.Address.ArrayIndex : uint.MaxValue));
                }

                specifications.Add(new BacnetReadAccessSpecification(first.Address.ObjectId, properties));
            }

            return specifications;
        }

        private static void ApplyBatchResponse(
            List<BacnetBatchItem> items,
            IList<BacnetReadAccessResult> response,
            PlcBatchReadResult[] results)
        {
            Dictionary<string, BacnetReadAccessResult> responseByObject = new Dictionary<string, BacnetReadAccessResult>(StringComparer.Ordinal);
            if (response != null)
            {
                for (int i = 0; i < response.Count; i++)
                {
                    BacnetReadAccessResult item = response[i];
                    responseByObject[GetObjectKey(item.objectIdentifier)] = item;
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                BacnetBatchItem item = items[i];
                try
                {
                    BacnetReadAccessResult objectResult;
                    if (!responseByObject.TryGetValue(item.ObjectKey, out objectResult) || objectResult.values == null)
                        throw new InvalidOperationException("BACnet batch response did not include object " + item.ObjectKey + ".");

                    BacnetPropertyValue propertyValue;
                    if (!TryFindPropertyValue(objectResult.values, item.Address, out propertyValue))
                        throw new InvalidOperationException("BACnet batch response did not include property " + item.Address.PropertyId + ".");

                    object value = ConvertReadValues(propertyValue.value, item.Request.DataType, item.Request.ElementCount);
                    PlcReadResult result = new PlcReadResult(0, item.Request.DataType.ToString(), value);
                    results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, result);
                }
                catch (Exception ex)
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
        }

        private void RetryBatchItemsIndividually(List<BacnetBatchItem> items, PlcBatchReadResult[] results)
        {
            for (int i = 0; i < items.Count; i++)
            {
                BacnetBatchItem item = items[i];
                try
                {
                    PlcReadResult result = Read(item.Request.Address, item.Request.DataType, item.Request.ElementCount, item.Request.ElementOffset);
                    results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, result);
                }
                catch (Exception ex)
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(
                        item.Request,
                        ex.Message,
                        IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                }
            }
        }

        private static bool TryFindPropertyValue(IList<BacnetPropertyValue> values, BacnetTagAddress address, out BacnetPropertyValue propertyValue)
        {
            propertyValue = default(BacnetPropertyValue);
            if (values == null)
                return false;

            bool hasFallback = false;
            BacnetPropertyValue fallback = default(BacnetPropertyValue);
            for (int i = 0; i < values.Count; i++)
            {
                BacnetPropertyValue value = values[i];
                if (value.property.propertyIdentifier != (uint)address.PropertyId)
                    continue;

                if (!address.HasArrayIndex)
                {
                    fallback = value;
                    hasFallback = true;
                    if (value.property.propertyArrayIndex == uint.MaxValue)
                    {
                        propertyValue = value;
                        return true;
                    }
                    continue;
                }

                if (value.property.propertyArrayIndex == address.ArrayIndex)
                {
                    propertyValue = value;
                    return true;
                }
            }

            if (!hasFallback)
                return false;

            propertyValue = fallback;
            return true;
        }

        private void EnsureConnected()
        {
            if (_client == null)
                throw new InvalidOperationException("BACnet/IP client is not connected.");
        }

        private static object ConvertReadValues(IList<BacnetValue> values, PlcDataType dataType, int elementCount)
        {
            if (values == null || values.Count == 0)
                return DefaultValue(dataType);

            if (IsArrayType(dataType) || elementCount > 1)
            {
                object[] rawValues = values.Select(item => item.Value).ToArray();
                return ConvertArray(rawValues, dataType);
            }

            return ConvertScalar(values[0].Value, dataType);
        }

        private static object ConvertArray(object[] values, PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                case PlcDataType.CoilArray:
                case PlcDataType.DiscreteInputArray:
                    return values.Select(ToBoolean).ToArray();
                case PlcDataType.Int16Array:
                    return values.Select(item => Convert.ToInt16(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.UInt16Array:
                    return values.Select(item => Convert.ToUInt16(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.Int32Array:
                    return values.Select(item => Convert.ToInt32(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.UInt32Array:
                    return values.Select(item => Convert.ToUInt32(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.Int64Array:
                    return values.Select(item => Convert.ToInt64(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.UInt64Array:
                    return values.Select(item => Convert.ToUInt64(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.FloatArray:
                    return values.Select(item => Convert.ToSingle(item, CultureInfo.InvariantCulture)).ToArray();
                case PlcDataType.DoubleArray:
                    return values.Select(item => Convert.ToDouble(item, CultureInfo.InvariantCulture)).ToArray();
                default:
                    return values.Select(item => ConvertScalar(item, dataType)).ToArray();
            }
        }

        private static object ConvertScalar(object value, PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    return ToBoolean(value);
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
                    return value;
            }
        }

        private static BacnetValue CreateWriteValue(BacnetTagAddress address, PlcDataType dataType, string valueText)
        {
            string text = valueText ?? string.Empty;
            if (IsBinaryPresentValue(address))
            {
                uint active = ToBoolean(text) ? 1u : 0u;
                return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, active);
            }

            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN, ToBoolean(text));
                case PlcDataType.Int16:
                case PlcDataType.Int32:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT, int.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int64:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT, long.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt16:
                case PlcDataType.UInt32:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, uint.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt64:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, ulong.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Float:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, float.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Double:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_DOUBLE, double.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.String:
                    return new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING, text);
                default:
                    return new BacnetValue(text);
            }
        }

        private static bool IsBinaryPresentValue(BacnetTagAddress address)
        {
            return address.PropertyId == BacnetPropertyIds.PROP_PRESENT_VALUE &&
                (address.ObjectId.Type == BacnetObjectTypes.OBJECT_BINARY_INPUT ||
                 address.ObjectId.Type == BacnetObjectTypes.OBJECT_BINARY_OUTPUT ||
                 address.ObjectId.Type == BacnetObjectTypes.OBJECT_BINARY_VALUE);
        }

        private static bool ToBoolean(object value)
        {
            if (value == null)
                return false;

            if (value is bool boolValue)
                return boolValue;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.Equals(text, "active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(text, "inactive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
                return false;

            double numeric;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numeric) && Math.Abs(numeric) > double.Epsilon;
        }

        private static bool IsArrayType(PlcDataType dataType)
        {
            return dataType == PlcDataType.BoolArray ||
                dataType == PlcDataType.Int16Array ||
                dataType == PlcDataType.UInt16Array ||
                dataType == PlcDataType.Int32Array ||
                dataType == PlcDataType.UInt32Array ||
                dataType == PlcDataType.Int64Array ||
                dataType == PlcDataType.UInt64Array ||
                dataType == PlcDataType.FloatArray ||
                dataType == PlcDataType.DoubleArray ||
                dataType == PlcDataType.CoilArray ||
                dataType == PlcDataType.DiscreteInputArray;
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException ||
                    current is OperationCanceledException)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private static string GetObjectKey(BacnetObjectId objectId)
        {
            return ((uint)objectId.Type).ToString(CultureInfo.InvariantCulture) + ":" +
                objectId.Instance.ToString(CultureInfo.InvariantCulture);
        }

        private static object DefaultValue(PlcDataType dataType)
        {
            if (dataType == PlcDataType.String)
                return string.Empty;
            if (IsArrayType(dataType))
                return Array.Empty<object>();
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.Coil || dataType == PlcDataType.DiscreteInput)
                return false;
            return 0;
        }

        private sealed class BacnetBatchItem
        {
            public BacnetBatchItem(int index, PlcBatchReadRequest request, BacnetTagAddress address)
            {
                Index = index;
                Request = request;
                Address = address;
                ObjectKey = GetObjectKey(address.ObjectId);
                PropertyKey = ((uint)address.PropertyId).ToString(CultureInfo.InvariantCulture) + ":" +
                    (address.HasArrayIndex ? address.ArrayIndex.ToString(CultureInfo.InvariantCulture) : "*");
            }

            public int Index { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
            public BacnetTagAddress Address { get; private set; }
            public string ObjectKey { get; private set; }
            public string PropertyKey { get; private set; }
        }

        private sealed class BacnetTagAddress
        {
            public BacnetObjectId ObjectId { get; private set; }
            public BacnetPropertyIds PropertyId { get; private set; }
            public uint ArrayIndex { get; private set; }
            public bool HasArrayIndex { get; private set; }

            public static BacnetTagAddress Parse(string address)
            {
                if (string.IsNullOrWhiteSpace(address))
                    throw new FormatException("BACnet tag address cannot be empty.");

                string[] parts = address.Split(new[] { ':', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    throw new FormatException("BACnet tag address must be objectType:instance[:property[:arrayIndex]].");

                BacnetObjectTypes objectType = ParseObjectType(parts[0]);
                uint instance = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                BacnetPropertyIds propertyId = parts.Length >= 3
                    ? ParsePropertyId(parts[2])
                    : BacnetPropertyIds.PROP_PRESENT_VALUE;

                BacnetTagAddress result = new BacnetTagAddress
                {
                    ObjectId = new BacnetObjectId(objectType, instance),
                    PropertyId = propertyId
                };

                if (parts.Length >= 4)
                {
                    result.ArrayIndex = uint.Parse(parts[3], CultureInfo.InvariantCulture);
                    result.HasArrayIndex = true;
                }

                return result;
            }

            private static BacnetObjectTypes ParseObjectType(string value)
            {
                string normalized = NormalizeToken(value);
                switch (normalized)
                {
                    case "ai":
                    case "analoginput":
                        return BacnetObjectTypes.OBJECT_ANALOG_INPUT;
                    case "ao":
                    case "analogoutput":
                        return BacnetObjectTypes.OBJECT_ANALOG_OUTPUT;
                    case "av":
                    case "analogvalue":
                        return BacnetObjectTypes.OBJECT_ANALOG_VALUE;
                    case "bi":
                    case "binaryinput":
                        return BacnetObjectTypes.OBJECT_BINARY_INPUT;
                    case "bo":
                    case "binaryoutput":
                        return BacnetObjectTypes.OBJECT_BINARY_OUTPUT;
                    case "bv":
                    case "binaryvalue":
                        return BacnetObjectTypes.OBJECT_BINARY_VALUE;
                    case "msi":
                    case "multistateinput":
                        return BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT;
                    case "mso":
                    case "multistateoutput":
                        return BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT;
                    case "msv":
                    case "multistatevalue":
                        return BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;
                    case "device":
                        return BacnetObjectTypes.OBJECT_DEVICE;
                }

                BacnetObjectTypes objectType;
                if (Enum.TryParse(value, true, out objectType) || Enum.TryParse("OBJECT_" + value, true, out objectType))
                    return objectType;

                uint numeric;
                if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
                    return (BacnetObjectTypes)numeric;

                throw new FormatException("Unsupported BACnet object type: " + value);
            }

            private static BacnetPropertyIds ParsePropertyId(string value)
            {
                string normalized = NormalizeToken(value);
                switch (normalized)
                {
                    case "pv":
                    case "presentvalue":
                        return BacnetPropertyIds.PROP_PRESENT_VALUE;
                    case "objectname":
                    case "name":
                        return BacnetPropertyIds.PROP_OBJECT_NAME;
                    case "description":
                        return BacnetPropertyIds.PROP_DESCRIPTION;
                    case "statusflags":
                        return BacnetPropertyIds.PROP_STATUS_FLAGS;
                    case "reliability":
                        return BacnetPropertyIds.PROP_RELIABILITY;
                    case "outofservice":
                        return BacnetPropertyIds.PROP_OUT_OF_SERVICE;
                    case "units":
                        return BacnetPropertyIds.PROP_UNITS;
                    case "priorityarray":
                        return BacnetPropertyIds.PROP_PRIORITY_ARRAY;
                }

                BacnetPropertyIds propertyId;
                if (Enum.TryParse(value, true, out propertyId) || Enum.TryParse("PROP_" + value, true, out propertyId))
                    return propertyId;

                uint numeric;
                if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
                    return (BacnetPropertyIds)numeric;

                throw new FormatException("Unsupported BACnet property: " + value);
            }

            private static string NormalizeToken(string value)
            {
                return new string((value ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
            }
        }

        private sealed class BacnetDriverOptions
        {
            public int LocalPort { get; private set; } = 0;
            public bool UseExclusivePort { get; private set; } = false;
            public bool DontFragment { get; private set; } = false;
            public int MaxPayload { get; private set; } = 1472;
            public string LocalEndpointIp { get; private set; } = string.Empty;
            public int Retries { get; private set; } = 1;
            public int WritePriority { get; private set; } = 16;
            public int MaxBatchObjects { get; private set; } = 16;

            public static BacnetDriverOptions Parse(string json)
            {
                BacnetDriverOptions options = new BacnetDriverOptions();
                if (string.IsNullOrWhiteSpace(json))
                    return options;

                try
                {
                    JsonDocument document = JsonDocument.Parse(json);
                    JsonElement root = document.RootElement;
                    options.LocalPort = ReadInt(root, "localPort", options.LocalPort, 0, 65535);
                    options.UseExclusivePort = ReadBool(root, "useExclusivePort", options.UseExclusivePort);
                    options.DontFragment = ReadBool(root, "dontFragment", options.DontFragment);
                    options.MaxPayload = ReadInt(root, "maxPayload", options.MaxPayload, 50, 1476);
                    options.LocalEndpointIp = ReadString(root, "localEndpointIp", options.LocalEndpointIp);
                    options.Retries = ReadInt(root, "retries", options.Retries, 0, 10);
                    options.WritePriority = ReadInt(root, "writePriority", options.WritePriority, 1, 16);
                    options.MaxBatchObjects = ReadInt(root, "maxBatchObjects", options.MaxBatchObjects, 1, 128);
                }
                catch (JsonException)
                {
                }

                return options;
            }

            private static int ReadInt(JsonElement root, string name, int fallback, int min, int max)
            {
                JsonElement value;
                if (!root.TryGetProperty(name, out value))
                    return fallback;

                int number;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
                    return Math.Min(max, Math.Max(min, number));
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                    return Math.Min(max, Math.Max(min, number));

                return fallback;
            }

            private static bool ReadBool(JsonElement root, string name, bool fallback)
            {
                JsonElement value;
                if (!root.TryGetProperty(name, out value))
                    return fallback;

                if (value.ValueKind == JsonValueKind.True)
                    return true;
                if (value.ValueKind == JsonValueKind.False)
                    return false;
                if (value.ValueKind == JsonValueKind.String)
                {
                    bool result;
                    if (bool.TryParse(value.GetString(), out result))
                        return result;
                }

                return fallback;
            }

            private static string ReadString(JsonElement root, string name, string fallback)
            {
                JsonElement value;
                if (!root.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String)
                    return fallback;
                return value.GetString() ?? fallback;
            }
        }
    }
}
