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
            string nodeId = NormalizeNodeId(address, elementOffset);
            DataValue value = ReadNodes(new[] { NodeId.Parse(nodeId) })[0];
            EnsureGoodStatus(value, nodeId);
            object converted = ConvertForRead(value == null ? null : value.Value, dataType, elementCount);
            return new PlcReadResult(0, dataType.ToString(), converted);
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            return RunSynchronousAsync(() => Read(address, dataType, elementCount, elementOffset), cancellationToken);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            EnsureConnected();
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return results;

            PlcBatchReadResult[] ordered = new PlcBatchReadResult[requests.Count];
            List<PendingRead> pending = new List<PendingRead>();
            List<NodeId> nodeIds = new List<NodeId>();

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    string nodeId = NormalizeNodeId(request.Address, request.ElementOffset);
                    pending.Add(new PendingRead(i, request, nodeId));
                    nodeIds.Add(NodeId.Parse(nodeId));
                }
                catch (Exception ex)
                {
                    ordered[i] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }

            if (pending.Count > 0)
            {
                int offset = 0;
                while (offset < pending.Count)
                {
                    int count = Math.Min(_maxNodesPerBatchRead, pending.Count - offset);
                    ReadPendingChunk(pending, nodeIds, ordered, offset, count);
                    offset += count;
                }
            }

            for (int i = 0; i < ordered.Length; i++)
                results.Add(ordered[i] ?? PlcBatchReadResult.FromFailure(EnsureRequest(requests[i]), "Batch read did not produce a result.", PlcReadFailureScope.Batch));

            return results;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            return RunSynchronousAsync(() => ReadMany(requests), cancellationToken);
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

        private void ReadPendingChunk(
            List<PendingRead> pending,
            List<NodeId> nodeIds,
            PlcBatchReadResult[] ordered,
            int offset,
            int count)
        {
            IList<DataValue> values;
            try
            {
                NodeId[] chunkNodeIds = new NodeId[count];
                nodeIds.CopyTo(offset, chunkNodeIds, 0, count);
                values = ReadNodes(chunkNodeIds);
            }
            catch (Exception ex)
            {
                bool sessionConnected = IsSessionStillConnected();
                if (ShouldSplitBatchReadException(ex, sessionConnected, count))
                {
                    int leftCount = count / 2;
                    ReadPendingChunk(pending, nodeIds, ordered, offset, leftCount);
                    ReadPendingChunk(pending, nodeIds, ordered, offset + leftCount, count - leftCount);
                    return;
                }

                PlcReadFailureScope failureScope = ClassifyBatchReadExceptionScope(ex, sessionConnected, count);
                if (failureScope == PlcReadFailureScope.Transport)
                    throw new PlcCommunicationException(ex.Message, ex);
                MarkPendingChunkFailure(pending, ordered, offset, count, ex.Message, failureScope);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                PendingRead read = pending[offset + i];
                if (ordered[read.Index] != null)
                    continue;

                try
                {
                    DataValue value = i < values.Count ? values[i] : null;
                    EnsureGoodStatus(value, read.NodeId);
                    object converted = ConvertForRead(value == null ? null : value.Value, read.Request.DataType, read.Request.ElementCount);
                    PlcReadResult result = new PlcReadResult(0, read.Request.DataType.ToString(), converted);
                    ordered[read.Index] = PlcBatchReadResult.FromSuccess(read.Request, result);
                }
                catch (Exception ex)
                {
                    ordered[read.Index] = PlcBatchReadResult.FromFailure(
                        read.Request,
                        ex.Message,
                        PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag));
                }
            }
        }

        private IList<DataValue> ReadNodes(IList<NodeId> nodeIds)
        {
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                nodesToRead.Add(new ReadValueId
                {
                    NodeId = nodeIds[i],
                    AttributeId = Attributes.Value
                });
            }

            ReadResponse response = _session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ClientBase.ValidateResponse(response.Results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToRead);
            return response.Results;
        }

        private static void MarkPendingChunkFailure(
            List<PendingRead> pending,
            PlcBatchReadResult[] ordered,
            int offset,
            int count,
            string errorMessage,
            PlcReadFailureScope failureScope)
        {
            for (int i = 0; i < count; i++)
            {
                PendingRead read = pending[offset + i];
                ordered[read.Index] = PlcBatchReadResult.FromFailure(read.Request, errorMessage, failureScope);
            }
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

        private static bool ShouldSplitBatchReadException(Exception exception, bool sessionConnected, int count)
        {
            if (count <= 1)
                return false;

            // KepServer can report a downstream device or node as unavailable while the OPC UA session is still healthy.
            return sessionConnected ||
                   LooksLikeOpcUaNodeOrDownstreamError(exception) ||
                   !IsSessionOrTransportException(exception);
        }

        private static bool ShouldTreatBatchReadExceptionAsCommunication(Exception exception, bool sessionConnected)
        {
            return PlcBatchReadResult.IsConnectionFailureScope(
                ClassifyBatchReadExceptionScope(exception, sessionConnected, 1));
        }

        private static PlcReadFailureScope ClassifyBatchReadExceptionScope(Exception exception, bool sessionConnected, int count)
        {
            if (sessionConnected || LooksLikeOpcUaNodeOrDownstreamError(exception))
                return count <= 1 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Batch;

            if (!IsSessionOrTransportException(exception))
                return count <= 1 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Batch;

            return LooksLikeSessionException(exception)
                ? PlcReadFailureScope.Session
                : PlcReadFailureScope.Transport;
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

        private static bool LooksLikeOpcUaNodeOrDownstreamError(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcTagException ||
                    current is PlcProtocolException protocolException &&
                    !PlcBatchReadResult.IsConnectionFailureScope(protocolException.FailureScope))
                    return true;
                if (current is ServiceResultException serviceException &&
                    !PlcBatchReadResult.IsConnectionFailureScope(ClassifyStatus(serviceException.StatusCode)))
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("badnodeid", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("nodeid", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("baddevice", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("badnotconnected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("badwaitingforinitialdata", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("badoutofservice", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("item", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unavailable", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            string nodeId = NormalizeNodeId(address, elementOffset);
            object value = ParseValue(dataType, valueText);
            WriteValueCollection nodesToWrite = new WriteValueCollection
            {
                new WriteValue
                {
                    NodeId = NodeId.Parse(nodeId),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(value))
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
                DataValue value = ReadNodes(new[] { NodeId.Parse(nodeId) })[0];
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

        private static void EnsureGoodStatus(DataValue value, string nodeId)
        {
            StatusCode status = value == null ? StatusCodes.BadNoData : value.StatusCode;
            if (StatusCode.IsBad(status))
                throw new PlcProtocolException(
                    ClassifyStatus(status),
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

        private sealed class PendingRead
        {
            public PendingRead(int index, PlcBatchReadRequest request, string nodeId)
            {
                Index = index;
                Request = request;
                NodeId = nodeId ?? string.Empty;
            }

            public int Index { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
            public string NodeId { get; private set; }
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
                       previous.ElementOffset == current.ElementOffset &&
                       string.Equals(previous.Address, current.Address, StringComparison.OrdinalIgnoreCase);
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

            private static void ConfigureMonitoredItem(
                MonitoredItem item,
                PlcSubscriptionRequest request,
                PlcSubscriptionOptions options)
            {
                item.DisplayName = string.IsNullOrWhiteSpace(request.Key) ? request.Address : request.Key;
                item.StartNodeId = NodeId.Parse(NormalizeNodeId(request.Address, request.ElementOffset));
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

                try
                {
                    MonitoredItemNotification notification = e?.NotificationValue as MonitoredItemNotification;
                    DataValue value = notification?.Value;
                    EnsureGoodStatus(value, NormalizeNodeId(request.Address, request.ElementOffset));
                    object converted = ConvertForRead(value == null ? null : value.Value, request.DataType, request.ElementCount);
                    PlcReadResult result = new PlcReadResult(0, request.DataType.ToString(), converted);
                    PublishUpdate(PlcSubscriptionUpdate.FromSuccess(request, result));
                }
                catch (Exception ex)
                {
                    PublishUpdate(PlcSubscriptionUpdate.FromFailure(
                        request,
                        ex.Message,
                        ClassifyBatchReadExceptionScope(ex, _owner.IsSessionStillConnected(), 1)));
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

            private static IList<PlcSubscriptionRequest> NormalizeRequests(IList<PlcSubscriptionRequest> requests)
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

                        result[request.Key] = request;
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
