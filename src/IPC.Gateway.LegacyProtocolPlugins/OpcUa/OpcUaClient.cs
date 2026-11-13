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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using Opc.Ua;
using Opc.Ua.Client;

namespace IPC.Plc.Communication.OpcUa
{
    
    
    
    
    
    
    
    
    
    public sealed class OpcUaClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient, IAsyncPlcSubscriptionClient
    {
        private const int DefaultMaxNodesPerBatchRead = 128;

        private readonly PlcConnectionOptions _options;
        private readonly List<OpcUaSubscription> _subscriptions = new List<OpcUaSubscription>();
        private ISession _session;
        private bool _connected;
        private int _maxNodesPerBatchRead = DefaultMaxNodesPerBatchRead;
        private int _maxMonitoredItemsPerCall = 1000;

        public OpcUaClient(PlcConnectionOptions options)
        {
            _options = options ?? new PlcConnectionOptions();
        }

        public bool IsConnected
        {
            get { return _connected && _session != null && _session.Connected; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.OpcUa; }
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            if (_session != null)
                Disconnect();

            EndpointDescription security = BuildSecurityPolicy();
            _session = OpcUaFoundationSessionFactory.Connect(
                _options,
                BuildEndpoint(),
                security.SecurityMode,
                security.SecurityPolicyUri);
            _connected = _session != null && _session.Connected;
            _maxNodesPerBatchRead = ReadServerOperationLimit("i=11705", DefaultMaxNodesPerBatchRead);
            _maxMonitoredItemsPerCall = ReadServerOperationLimit("i=11714", 1000);
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
                return ValueTask.CompletedTask;

            return RunSynchronousAsync(Connect, cancellationToken);
        }

        public void Disconnect()
        {
            DisposeSubscriptions();
            if (_session != null)
            {
                try
                {
                    _session.CloseAsync(
                            Math.Max(1000, _options.TimeoutMilliseconds),
                            true,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                }

                try
                {
                    _session.Dispose();
                }
                catch
                {
                }
            }

            _session = null;
            _connected = false;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                _connected = false;
                return ValueTask.CompletedTask;
            }

            return RunSynchronousAsync(Disconnect, cancellationToken);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            return ReadOneAsync(address, dataType, elementCount, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public ValueTask<PlcReadResult> ReadAsync(string address, PlcDataType dataType, int elementCount, int elementOffset, CancellationToken cancellationToken)
            => ReadOneAsync(address, dataType, elementCount, elementOffset, cancellationToken);

        private async ValueTask<PlcReadResult> ReadOneAsync(string address, PlcDataType type, int count, int offset, CancellationToken token)
        {
            EnsureConnected();
            var node = CreateReadNode(address, type, count, offset);
            var values = await ReadNodesAsync(new ReadValueIdCollection { node }, token).ConfigureAwait(false);
            return DecodeValue(values[0], address, type, count, offset);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
            => ReadManyAsync(requests, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(IList<PlcBatchReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            if (requests == null || requests.Count == 0) return Array.Empty<PlcBatchReadResult>();
            var ordered = new PlcBatchReadResult[requests.Count];
            var pending = new List<(int Index, PlcBatchReadRequest Request, ReadValueId Node)>();
            for (int i = 0; i < requests.Count; i++)
            {
                var request = EnsureRequest(requests[i]);
                try
                {
                    var node = CreateReadNode(request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                    pending.Add((i, request, node));
                }
                catch (Exception ex) { ordered[i] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag); }
            }

            var recovery = PlcBatchReadRecovery.Start(_session, "OPC UA");
            try
            {
                for (int offset = 0; offset < pending.Count; offset += _maxNodesPerBatchRead)
                {
                    int end = Math.Min(offset + _maxNodesPerBatchRead, pending.Count);
                    await recovery.ReadRangeAsync(pending, offset, end, item => item.Request, item => item.Index,
                        ReadRange, ordered, "Value", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { PlcBatchReadResult.DeferPending(requests, ordered, ex.Message); }
            return ordered;

            async ValueTask ReadRange(int start, int end)
            {
                var nodes = new ReadValueIdCollection();
                for (int i = start; i < end; i++) nodes.Add(pending[i].Node);
                IList<DataValue> values;
                try { values = await ReadNodesAsync(nodes, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new PlcProtocolException(ClassifyBatchReadExceptionScope(ex, IsSessionStillConnected(), end - start), ex.Message);
                }
                for (int i = start; i < end; i++)
                {
                    var (index, request, _) = pending[i];
                    try
                    {
                        var result = DecodeValue(values[i - start], request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                        ordered[index] = PlcBatchReadResult.FromSuccess(request, result);
                    }
                    catch (Exception ex) { ordered[index] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag)); }
                }
            }
        }

        public ValueTask<IPlcSubscription> SubscribeAsync(
            IList<PlcSubscriptionRequest> requests,
            PlcSubscriptionOptions options,
            Func<PlcSubscriptionUpdate, ValueTask> onUpdate,
            CancellationToken cancellationToken)
        {
            if (requests == null)
                throw new ArgumentNullException("requests");
            if (options == null)
                throw new ArgumentNullException("options");
            if (onUpdate == null)
                throw new ArgumentNullException("onUpdate");

            return RunSynchronousAsync<IPlcSubscription>(() =>
            {
                EnsureConnected();
                OpcUaSubscription subscription = new OpcUaSubscription(this, onUpdate);
                subscription.Update(requests, options);
                _subscriptions.Add(subscription);
                return subscription;
            }, cancellationToken);
        }

        private ReadValueId CreateReadNode(string address, PlcDataType type, int count, int offset)
            => new ReadValueId { NodeId = ResolveNodeId(address), AttributeId = Attributes.Value, IndexRange = GetIndexRange(type, count, offset) };

        private NodeId ResolveNodeId(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new FormatException("OPC UA NodeId is empty.");
            return ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(address.Trim()), _session.NamespaceUris)
                ?? throw new FormatException("Unknown OPC UA namespace: " + address);
        }

        private static string GetIndexRange(PlcDataType type, int count, int offset)
        {
            if (offset < 0 || count < 1) throw new ArgumentOutOfRangeException(nameof(offset));
            if (!PlcDataTypeHelper.IsArray(type) && offset == 0) return null;
            int last = checked(offset + (PlcDataTypeHelper.IsArray(type) || type == PlcDataType.String ? count - 1 : 0));
            return offset == last ? offset.ToString(CultureInfo.InvariantCulture)
                : FormattableString.Invariant($"{offset}:{last}");
        }

        private static PlcReadResult DecodeValue(DataValue sample, string address, PlcDataType type, int count, int offset)
        {
            EnsureGoodStatus(sample, address);
            object raw = sample.Value;
            if (offset > 0 && !PlcDataTypeHelper.IsArray(type) && raw is Array array && array.Length == 1)
                raw = array.GetValue(0);
            if (raw == null) throw new PlcTagException("OPC UA node returned no value: " + address);
            if (PlcDataTypeHelper.IsArray(type) && (raw is not IList list || list.Count != count))
                throw new PlcTagException("OPC UA array result length differs from the requested range: " + address);
            return new PlcReadResult(0, type.ToString(), ConvertForRead(raw, type, count))
            {
                IsUncertain = StatusCode.IsUncertain(sample.StatusCode),
                SourceTimestampUtc = GetSourceTime(sample)
            };
        }

        private static DateTime? GetSourceTime(DataValue value)
            => value == null || value.SourceTimestamp == DateTime.MinValue ? null : value.SourceTimestamp.ToUniversalTime();

        private async ValueTask<IList<DataValue>> ReadNodesAsync(ReadValueIdCollection nodes, CancellationToken token)
        {
            ReadResponse response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both, nodes, token).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodes);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodes);
            return response.Results;
        }

        private bool IsSessionStillConnected()
        {
            try
            {
                return IsConnected;
            }
            catch
            {
                return false;
            }
        }

        private static PlcReadFailureScope ClassifyBatchReadExceptionScope(Exception exception, bool sessionConnected, int count)
        {
            if (exception is PlcProtocolException protocol) return protocol.FailureScope;
            if (exception is OperationCanceledException || IsSessionOrTransportException(exception) || !sessionConnected)
                return LooksLikeSessionException(exception) ? PlcReadFailureScope.Session : PlcReadFailureScope.Transport;
            return count <= 1 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Batch;
        }

        private static bool IsSessionOrTransportException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcProtocolException protocolException &&
                    PlcBatchReadResult.IsConnectionFailureScope(protocolException.FailureScope))
                    return true;
                if (current is ServiceResultException serviceException &&
                    PlcBatchReadResult.IsConnectionFailureScope(ClassifyStatus(serviceException.StatusCode)))
                    return true;
                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("connection refused", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("secure channel", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("session", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("opc ua client is not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("disconnected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("server not connected", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static bool LooksLikeSessionException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcProtocolException protocolException &&
                    protocolException.FailureScope == PlcReadFailureScope.Session)
                    return true;
                if (current is ServiceResultException serviceException &&
                    ClassifyStatus(serviceException.StatusCode) == PlcReadFailureScope.Session)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("session", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("secure channel", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("opc ua client is not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("server not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("disconnected", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            string nodeId = address;
            object value = ParseValue(dataType, valueText);
            WriteValueCollection nodesToWrite = new WriteValueCollection
            {
                new WriteValue
                {
                    NodeId = ResolveNodeId(nodeId),
                    IndexRange = GetIndexRange(dataType, value is Array array ? array.Length : value is string text ? Math.Max(1, text.Length) : 1, elementOffset),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(CreateWriteVariant(dataType, value, elementOffset))
                }
            };

            WriteResponse response = _session.WriteAsync(null, nodesToWrite, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ClientBase.ValidateResponse(response.Results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToWrite);
            if (response.Results.Count == 0 || StatusCode.IsBad(response.Results[0]))
            {
                StatusCode status = response.Results.Count == 0
                    ? StatusCodes.BadUnexpectedError
                    : response.Results[0];
                throw new PlcProtocolException(
                    ClassifyStatus(status),
                    "OPC UA write failed for " + nodeId + ": " + status,
                    status.ToString());
            }
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            return RunSynchronousAsync(() => Write(address, dataType, valueText, elementOffset), cancellationToken);
        }

        public void Dispose()
        {
            if (_subscriptions.Count > 0 || ShouldCloseSession())
                Disconnect();
            else if (_session != null)
                _session.Dispose();
            _session = null;
            _connected = false;
        }

        private void DisposeSubscriptions()
        {
            if (_subscriptions.Count == 0)
                return;

            OpcUaSubscription[] subscriptions = _subscriptions.ToArray();
            _subscriptions.Clear();
            for (int i = 0; i < subscriptions.Length; i++)
            {
                try
                {
                    subscriptions[i].Dispose();
                }
                catch
                {
                }
            }
        }

        private bool ShouldCloseSession()
        {
            if (_session == null)
                return false;
            if (_connected)
                return true;

            try
            {
                return _session.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static ValueTask RunSynchronousAsync(Action action, CancellationToken cancellationToken)
        {
            return PlcClientInvoker.InvokeSynchronousAsync(action, cancellationToken);
        }

        private static ValueTask<T> RunSynchronousAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            return PlcClientInvoker.InvokeSynchronousAsync(action, cancellationToken);
        }

        private string BuildEndpoint()
        {
            string host = string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host.Trim();
            int port = _options.Port <= 0 ? 4840 : _options.Port;

            if (host.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return BuildEndpointFromUri(host, port);

            return BuildEndpointFromUri("opc.tcp://" + host, port);
        }

        private EndpointDescription BuildSecurityPolicy()
        {
            MessageSecurityMode mode = ParseSecurityMode(_options.OpcUaMessageSecurityMode);
            string policyUri = ParseSecurityPolicyUri(_options.OpcUaSecurityPolicy);
            if (mode == MessageSecurityMode.None && policyUri != SecurityPolicies.None)
                throw new ArgumentException("OPC UA message security mode None requires security policy None.");
            if (mode != MessageSecurityMode.None && policyUri == SecurityPolicies.None)
                throw new ArgumentException("OPC UA Sign or SignAndEncrypt requires a secure security policy.");

            return new EndpointDescription
            {
                SecurityMode = mode,
                SecurityPolicyUri = policyUri
            };
        }

        private static MessageSecurityMode ParseSecurityMode(string value)
        {
            string normalized = NormalizeSecurityName(value);
            if (normalized == "NONE")
                return MessageSecurityMode.None;
            if (normalized == "SIGN")
                return MessageSecurityMode.Sign;
            if (normalized == "SIGNANDENCRYPT")
                return MessageSecurityMode.SignAndEncrypt;
            throw new ArgumentException("Unsupported OPC UA message security mode: " + (value ?? string.Empty));
        }

        private static string ParseSecurityPolicyUri(string value)
        {
            string normalized = NormalizeSecurityName(value);
            if (normalized == "NONE")
                return SecurityPolicies.None;
            if (normalized == "BASIC128RSA15")
                return SecurityPolicies.Basic128Rsa15;
            if (normalized == "BASIC256")
                return SecurityPolicies.Basic256;
            if (normalized == "BASIC256SHA256")
                return SecurityPolicies.Basic256Sha256;
            if (normalized == "AES128SHA256RSAOAEP")
                return SecurityPolicies.Aes128_Sha256_RsaOaep;
            if (normalized == "AES256SHA256RSAPSS")
                return SecurityPolicies.Aes256_Sha256_RsaPss;
            throw new ArgumentException("Unsupported OPC UA security policy: " + (value ?? string.Empty));
        }

        private static string NormalizeSecurityName(string value)
        {
            string text = (value ?? "None").Trim();
            int separator = text.LastIndexOf('#');
            if (separator >= 0 && separator + 1 < text.Length)
                text = text.Substring(separator + 1);
            return text.Replace("_", string.Empty)
                       .Replace("-", string.Empty)
                       .Replace(" ", string.Empty)
                       .ToUpperInvariant();
        }

        private int ReadServerOperationLimit(string nodeId, int fallback)
        {
            try
            {
                DataValue value = ReadNodesAsync(new ReadValueIdCollection { new ReadValueId { NodeId = NodeId.Parse(nodeId), AttributeId = Attributes.Value } }, CancellationToken.None).AsTask().GetAwaiter().GetResult()[0];
                if (value == null || StatusCode.IsBad(value.StatusCode))
                    return fallback;
                int limit = Convert.ToInt32(value.Value, CultureInfo.InvariantCulture);
                return limit > 0 ? Math.Clamp(limit, 1, 10000) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildEndpointFromUri(string endpoint, int port)
        {
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
                return endpoint;

            if (!uri.IsDefaultPort)
                return endpoint;

            UriBuilder builder = new UriBuilder(uri)
            {
                Port = port
            };
            return builder.ToString();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("OPC UA client is not connected.");
        }

        private static void EnsureGoodStatus(DataValue value, string nodeId)
        {
            StatusCode status = value == null ? StatusCodes.BadNoData : value.StatusCode;
            if (StatusCode.IsBad(status))
                throw new PlcProtocolException(
                    PlcReadFailureScope.Tag,
                    "OPC UA 读取失败 " + nodeId + ": " + status,
                    status.ToString());
        }

        private static PlcReadFailureScope ClassifyStatus(StatusCode statusCode)
        {
            uint code = statusCode.Code;
            if (code == StatusCodes.BadSessionClosed ||
                code == StatusCodes.BadSessionIdInvalid ||
                code == StatusCodes.BadSessionNotActivated)
                return PlcReadFailureScope.Session;

            if (code == StatusCodes.BadCommunicationError ||
                code == StatusCodes.BadConnectionClosed ||
                code == StatusCodes.BadConnectionRejected ||
                code == StatusCodes.BadSecureChannelClosed ||
                code == StatusCodes.BadSecureChannelIdInvalid ||
                code == StatusCodes.BadSecureChannelTokenUnknown ||
                code == StatusCodes.BadServerNotConnected ||
                code == StatusCodes.BadTcpSecureChannelUnknown ||
                code == StatusCodes.BadTimeout)
                return PlcReadFailureScope.Transport;

            return PlcReadFailureScope.Tag;
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
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

        private static Variant CreateWriteVariant(PlcDataType dataType, object value, int offset)
        {
            if (offset > 0 && value is not Array && dataType != PlcDataType.String)
            {
                Array single = Array.CreateInstance(value.GetType(), 1);
                single.SetValue(value, 0);
                return dataType == PlcDataType.Byte || dataType == PlcDataType.UInt8
                    ? new Variant(single, new TypeInfo(BuiltInType.Byte, ValueRanks.OneDimension))
                    : new Variant(single);
            }
            // OPC UA distinguishes Byte[] from the scalar ByteString, although
            // both use byte[] in .NET. Preserve the configured array type on write.
            return dataType == PlcDataType.ByteArray || dataType == PlcDataType.UInt8Array
                ? new Variant(value, new TypeInfo(BuiltInType.Byte, ValueRanks.OneDimension))
                : new Variant(value);
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
                case PlcDataType.ByteArray:
                    return PlcDataType.Byte;
                case PlcDataType.UInt8Array:
                    return PlcDataType.UInt8;
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
                case PlcDataType.Byte:
                case PlcDataType.UInt8:
                    return Convert.ToByte(value, CultureInfo.InvariantCulture);
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

        private sealed class OpcUaSubscription : IPlcSubscription
        {
            private readonly OpcUaClient _owner;
            private readonly Func<PlcSubscriptionUpdate, ValueTask> _onUpdate;
            private readonly Dictionary<string, PlcSubscriptionRequest> _requestsByKey =
                new Dictionary<string, PlcSubscriptionRequest>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, MonitoredItem> _itemsByKey =
                new Dictionary<string, MonitoredItem>(StringComparer.OrdinalIgnoreCase);
            private Subscription _subscription;
            private bool _disposed;

            public OpcUaSubscription(OpcUaClient owner, Func<PlcSubscriptionUpdate, ValueTask> onUpdate)
            {
                _owner = owner;
                _onUpdate = onUpdate;
            }

            public bool IsActive
            {
                get
                {
                    return !_disposed &&
                           _subscription != null &&
                           _subscription.Created &&
                           _subscription.PublishingEnabled &&
                           _owner.IsConnected;
                }
            }

            public IReadOnlyCollection<string> MonitoredKeys
            {
                get { return _itemsByKey.Keys.ToArray(); }
            }

            public ValueTask UpdateAsync(
                IList<PlcSubscriptionRequest> requests,
                PlcSubscriptionOptions options,
                CancellationToken cancellationToken)
            {
                return RunSynchronousAsync(() => Update(requests, options), cancellationToken);
            }

            public void Update(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options)
            {
                if (_disposed)
                    throw new ObjectDisposedException("OPC UA subscription");

                _owner.EnsureConnected();
                IList<PlcSubscriptionRequest> normalizedRequests = NormalizeRequests(requests);
                PlcSubscriptionOptions normalizedOptions = NormalizeOptions(options);

                if (normalizedRequests.Count == 0)
                {
                    Dispose();
                    return;
                }

                if (_subscription == null)
                    CreateSubscription(normalizedRequests, normalizedOptions);
                else
                    ReconcileSubscription(normalizedRequests, normalizedOptions);

                PublishItemStatusFailures();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_subscription != null)
                {
                    try
                    {
                        foreach (MonitoredItem item in _itemsByKey.Values)
                            item.Notification -= OnNotification;
                        if (_subscription.Created)
                        {
                            _subscription.DeleteAsync(true, CancellationToken.None)
                                .GetAwaiter()
                                .GetResult();
                        }
                        if (_owner._session != null)
                        {
                            _owner._session.RemoveSubscriptionAsync(_subscription, CancellationToken.None)
                                .GetAwaiter()
                                .GetResult();
                        }
                    }
                    catch
                    {
                    }

                    _subscription.Dispose();
                }

                _itemsByKey.Clear();
                _requestsByKey.Clear();
                _subscription = null;
                _owner._subscriptions.Remove(this);
            }

            private void CreateSubscription(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options)
            {
                _subscription = new Subscription(_owner._session.DefaultSubscription);
                ConfigureSubscription(_subscription, requests.Count, options);

                int firstCount = Math.Min(requests.Count, Math.Max(1, _owner._maxMonitoredItemsPerCall));
                for (int i = 0; i < firstCount; i++)
                {
                    MonitoredItem item = CreateMonitoredItem(requests[i], options);
                    _subscription.AddItem(item);
                    TrackItem(requests[i], item);
                }

                _owner._session.AddSubscription(_subscription);
                _subscription.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();

                int addedInCall = 0;
                for (int index = firstCount; index < requests.Count; index++)
                {
                    PlcSubscriptionRequest request = requests[index];
                    MonitoredItem item = CreateMonitoredItem(request, options);
                    _subscription.AddItem(item);
                    TrackItem(request, item);
                    addedInCall++;
                    if (addedInCall >= _owner._maxMonitoredItemsPerCall || index == requests.Count - 1)
                    {
                        _subscription.ApplyChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
                        addedInCall = 0;
                    }
                }

                if (!_subscription.PublishingEnabled)
                    _subscription.SetPublishingModeAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            }

            private void ReconcileSubscription(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options)
            {
                Dictionary<string, PlcSubscriptionRequest> target =
                    new Dictionary<string, PlcSubscriptionRequest>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < requests.Count; i++)
                    target[requests[i].Key] = requests[i];

                List<MonitoredItem> removeItems = new List<MonitoredItem>();
                foreach (KeyValuePair<string, MonitoredItem> pair in _itemsByKey.ToArray())
                {
                    if (target.ContainsKey(pair.Key))
                        continue;

                    pair.Value.Notification -= OnNotification;
                    removeItems.Add(pair.Value);
                    _itemsByKey.Remove(pair.Key);
                    _requestsByKey.Remove(pair.Key);
                }

                if (removeItems.Count > 0)
                    _subscription.RemoveItems(removeItems);

                int pendingAdds = 0;
                for (int i = 0; i < requests.Count; i++)
                {
                    PlcSubscriptionRequest request = requests[i];
                    MonitoredItem existing;
                    if (_itemsByKey.TryGetValue(request.Key, out existing))
                    {
                        PlcSubscriptionRequest previous = _requestsByKey[request.Key];
                        if (HasSameMonitoredTarget(previous, request))
                        {
                            ConfigureMonitoredItem(existing, request, options);
                            _requestsByKey[request.Key] = request;
                            continue;
                        }

                        existing.Notification -= OnNotification;
                        _subscription.RemoveItem(existing);
                        _itemsByKey.Remove(request.Key);
                        _requestsByKey.Remove(request.Key);
                    }

                    MonitoredItem item = CreateMonitoredItem(request, options);
                    _subscription.AddItem(item);
                    TrackItem(request, item);
                    pendingAdds++;
                    if (pendingAdds >= _owner._maxMonitoredItemsPerCall)
                    {
                        _subscription.ApplyChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
                        pendingAdds = 0;
                    }
                }

                ConfigureSubscription(_subscription, requests.Count, options);
                _subscription.ModifyAsync(CancellationToken.None).GetAwaiter().GetResult();
                _subscription.ApplyChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (!_subscription.PublishingEnabled)
                    _subscription.SetPublishingModeAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            }

            private static bool HasSameMonitoredTarget(
                PlcSubscriptionRequest previous,
                PlcSubscriptionRequest current)
            {
                return previous != null &&
                       current != null &&
                       previous.ElementCount == current.ElementCount && previous.DataType == current.DataType &&
                       previous.ElementOffset == current.ElementOffset &&
                       string.Equals(previous.Address, current.Address, StringComparison.Ordinal);
            }

            private void TrackItem(PlcSubscriptionRequest request, MonitoredItem item)
            {
                _requestsByKey[request.Key] = request;
                _itemsByKey[request.Key] = item;
            }

            private MonitoredItem CreateMonitoredItem(
                PlcSubscriptionRequest request,
                PlcSubscriptionOptions options)
            {
                MonitoredItem item = new MonitoredItem(_subscription.DefaultItem);
                ConfigureMonitoredItem(item, request, options);
                item.Notification += OnNotification;
                return item;
            }

            private static void ConfigureSubscription(
                Subscription subscription,
                int requestCount,
                PlcSubscriptionOptions options)
            {
                subscription.DisplayName = "IPC Gateway OPC UA";
                subscription.PublishingInterval = options.PublishingIntervalMs;
                subscription.MaxNotificationsPerPublish = (uint)Math.Max(1, requestCount);
                subscription.PublishingEnabled = true;
            }

            private void ConfigureMonitoredItem(
                MonitoredItem item,
                PlcSubscriptionRequest request,
                PlcSubscriptionOptions options)
            {
                item.DisplayName = string.IsNullOrWhiteSpace(request.Key) ? request.Address : request.Key;
                item.StartNodeId = _owner.ResolveNodeId(request.Address);
                item.IndexRange = GetIndexRange(request.DataType, request.ElementCount, request.ElementOffset);
                item.AttributeId = Attributes.Value;
                item.MonitoringMode = MonitoringMode.Reporting;
                item.SamplingInterval = Math.Max(100, request.SamplingIntervalMs > 0 ? request.SamplingIntervalMs : options.SamplingIntervalMs);
                item.QueueSize = (uint)Math.Max(1, options.QueueSize);
                item.DiscardOldest = options.DiscardOldest;
                item.Handle = request;
            }

            private void OnNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
            {
                PlcSubscriptionRequest request = ResolveRequest(monitoredItem);
                if (request == null)
                    return;

                DataValue value = (e?.NotificationValue as MonitoredItemNotification)?.Value;
                try
                {
                    PlcReadResult result = DecodeValue(value, request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                    PublishUpdate(PlcSubscriptionUpdate.FromSuccess(request, result));
                }
                catch (Exception ex)
                {
                    PublishUpdate(PlcSubscriptionUpdate.FromFailure(
                        request,
                        ex.Message,
                        ClassifyBatchReadExceptionScope(ex, _owner.IsSessionStillConnected(), 1), GetSourceTime(value)));
                }
            }

            private PlcSubscriptionRequest ResolveRequest(MonitoredItem monitoredItem)
            {
                PlcSubscriptionRequest request = monitoredItem?.Handle as PlcSubscriptionRequest;

                if (request != null)
                    return request;

                foreach (KeyValuePair<string, MonitoredItem> pair in _itemsByKey)
                {
                    if (ReferenceEquals(pair.Value, monitoredItem))
                    {
                        PlcSubscriptionRequest found;
                        return _requestsByKey.TryGetValue(pair.Key, out found) ? found : null;
                    }
                }

                return null;
            }

            private void PublishItemStatusFailures()
            {
                foreach (KeyValuePair<string, MonitoredItem> pair in _itemsByKey)
                {
                    MonitoredItem item = pair.Value;
                    if (item == null || item.Status == null || !ServiceResult.IsBad(item.Status.Error))
                        continue;

                    PlcSubscriptionRequest request;
                    if (!_requestsByKey.TryGetValue(pair.Key, out request))
                        continue;

                    string message = item.Status.Error.ToString();
                    PublishUpdate(PlcSubscriptionUpdate.FromFailure(
                        request,
                        message,
                        ClassifyStatus(item.Status.Error.StatusCode)));
                }
            }

            private void PublishUpdate(PlcSubscriptionUpdate update)
            {
                try
                {
                    _ = PublishUpdateAsync(update);
                }
                catch
                {
                }
            }

            private async Task PublishUpdateAsync(PlcSubscriptionUpdate update)
            {
                try
                {
                    await _onUpdate(update).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            private IList<PlcSubscriptionRequest> NormalizeRequests(IList<PlcSubscriptionRequest> requests)
            {
                Dictionary<string, PlcSubscriptionRequest> result =
                    new Dictionary<string, PlcSubscriptionRequest>(StringComparer.OrdinalIgnoreCase);
                if (requests != null)
                {
                    for (int i = 0; i < requests.Count; i++)
                    {
                        PlcSubscriptionRequest request = requests[i];
                        if (request == null || string.IsNullOrWhiteSpace(request.Address))
                            continue;

                        try
                        {
                            _owner.CreateReadNode(request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                            result[request.Key] = request;
                        }
                        catch (Exception ex) { PublishUpdate(PlcSubscriptionUpdate.FromFailure(request, ex.Message, PlcReadFailureScope.Tag)); }
                    }
                }

                return result.Values.ToList();
            }

            private static PlcSubscriptionOptions NormalizeOptions(PlcSubscriptionOptions options)
            {
                PlcSubscriptionOptions normalized = options ?? new PlcSubscriptionOptions();
                normalized.PublishingIntervalMs = Math.Max(100, normalized.PublishingIntervalMs);
                normalized.SamplingIntervalMs = Math.Max(100, normalized.SamplingIntervalMs);
                normalized.QueueSize = Math.Max(1, normalized.QueueSize);
                return normalized;
            }
        }
    }
}
