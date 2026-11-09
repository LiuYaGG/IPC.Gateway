using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace IPC.Plc.Communication.Snmp
{
    public sealed class SnmpClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _connection;
        private readonly SnmpDriverOptions _options;
        private IPEndPoint _endpoint;
        private ISnmpMessage _v3Report;
        private bool _connected;

        public SnmpClient(PlcConnectionOptions connection)
        {
            _connection = connection ?? new PlcConnectionOptions();
            _options = SnmpDriverOptions.Parse(_connection);
        }

        public bool IsConnected => _connected;
        public PlcProtocol Protocol => PlcProtocol.Snmp;

        public void Connect() => ConnectAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            if (_connected)
                return;
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(_connection.Host, cancellationToken).ConfigureAwait(false);
            IPAddress address = addresses.FirstOrDefault(item => item.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault()
                ?? throw new PlcCommunicationException("无法解析 SNMP 设备地址。");
            _endpoint = new IPEndPoint(address, _connection.Port > 0 ? _connection.Port : 161);
            _v3Report = null;
            IList<Variable> probe = await ExecuteGetAsync(
                new List<Variable> { new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.3.0")) },
                cancellationToken).ConfigureAwait(false);
            if (probe.Count != 1)
                throw new PlcCommunicationException("SNMP 在线探测未返回 sysUpTime。");
            _connected = true;
        }

        public void Disconnect()
        {
            _connected = false;
            _v3Report = null;
            _endpoint = null;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
            => ReadAsync(address, dataType, elementCount, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            if (elementCount != 1 || elementOffset != 0)
                throw new NotSupportedException("SNMP OID 读取不使用元素数量或偏移。");
            string oid = SnmpAddress.Parse(address);
            IList<Variable> values = await ExecuteGetAsync(
                new List<Variable> { new Variable(new ObjectIdentifier(oid)) },
                cancellationToken).ConfigureAwait(false);
            if (values.Count != 1)
                throw new SnmpTagException("SNMP 响应中缺少 OID " + oid + "。");
            return Decode(values[0], dataType);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
            => ReadManyAsync(requests, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (requests == null || requests.Count == 0)
                return new List<PlcBatchReadResult>();
            EnsureConnected();

            PlcBatchReadResult[] ordered = new PlcBatchReadResult[requests.Count];
            List<int> validIndexes = new List<int>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i];
                try
                {
                    if (request.ElementCount != 1 || request.ElementOffset != 0)
                        throw new NotSupportedException("SNMP OID 不使用元素数量或偏移。");
                    _ = SnmpAddress.Parse(request.Address);
                    validIndexes.Add(i);
                }
                catch (Exception ex) when (IsTagError(ex))
                {
                    ordered[i] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }

            for (int offset = 0; offset < validIndexes.Count; offset += _options.MaxOidsPerRequest)
            {
                int count = Math.Min(_options.MaxOidsPerRequest, validIndexes.Count - offset);
                await ReadChunkAsync(requests, validIndexes, ordered, offset, count, cancellationToken).ConfigureAwait(false);
            }
            return ordered;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
            => WriteAsync(address, dataType, valueText, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            EnsureConnected();
            if (elementOffset != 0)
                throw new NotSupportedException("SNMP SET 不使用元素偏移。");
            Variable variable = new Variable(new ObjectIdentifier(SnmpAddress.Parse(address)), SnmpDataCodec.Encode(dataType, valueText));
            await ExecuteSetAsync(new List<Variable> { variable }, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => Disconnect();

        private async ValueTask<IList<Variable>> ExecuteGetAsync(IList<Variable> variables, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                VersionCode version = GetVersion();
                if (version != VersionCode.V3)
                    return await Messenger.GetAsync(version, _endpoint, new OctetString(_options.Community), variables, timeout.Token).ConfigureAwait(false);

                ISnmpMessage report = await GetV3ReportAsync(SnmpType.GetRequestPdu, timeout.Token).ConfigureAwait(false);
                GetRequestMessage request = new GetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(_options.UserName),
                    new OctetString(_options.ContextName),
                    variables,
                    BuildPrivacyProvider(),
                    Messenger.MaxMessageSize,
                    report);
                ISnmpMessage response = await request.GetResponseAsync(_endpoint, timeout.Token).ConfigureAwait(false);
                EnsureResponseSucceeded(response);
                _v3Report = response;
                return response.Pdu().Variables;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new System.TimeoutException("SNMP 请求超时。");
            }
        }

        private async ValueTask ReadChunkAsync(
            IList<PlcBatchReadRequest> requests,
            List<int> validIndexes,
            PlcBatchReadResult[] ordered,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            List<Variable> variables = new List<Variable>(count);
            for (int i = 0; i < count; i++)
            {
                PlcBatchReadRequest request = requests[validIndexes[offset + i]];
                variables.Add(new Variable(new ObjectIdentifier(SnmpAddress.Parse(request.Address))));
            }

            IList<Variable> values;
            try
            {
                values = await ExecuteGetAsync(variables, cancellationToken).ConfigureAwait(false);
            }
            catch (ErrorException) when (count > 1)
            {
                int left = count / 2;
                await ReadChunkAsync(requests, validIndexes, ordered, offset, left, cancellationToken).ConfigureAwait(false);
                await ReadChunkAsync(requests, validIndexes, ordered, offset + left, count - left, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ErrorException ex)
            {
                int index = validIndexes[offset];
                ordered[index] = PlcBatchReadResult.FromFailure(requests[index], ex.Message, PlcReadFailureScope.Tag);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int index = validIndexes[offset + i];
                PlcBatchReadRequest request = requests[index];
                try
                {
                    if (i >= values.Count)
                        throw new SnmpTagException("SNMP 批量响应缺少对应 OID。");
                    ordered[index] = PlcBatchReadResult.FromSuccess(request, Decode(values[i], request.DataType));
                }
                catch (Exception ex) when (IsTagError(ex))
                {
                    ordered[index] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
        }

        private async ValueTask ExecuteSetAsync(IList<Variable> variables, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                VersionCode version = GetVersion();
                if (version != VersionCode.V3)
                {
                    await Messenger.SetAsync(version, _endpoint, new OctetString(_options.Community), variables, timeout.Token).ConfigureAwait(false);
                    return;
                }

                ISnmpMessage report = await GetV3ReportAsync(SnmpType.SetRequestPdu, timeout.Token).ConfigureAwait(false);
                SetRequestMessage request = new SetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(_options.UserName),
                    new OctetString(_options.ContextName),
                    variables,
                    BuildPrivacyProvider(),
                    Messenger.MaxMessageSize,
                    report);
                ISnmpMessage response = await request.GetResponseAsync(_endpoint, timeout.Token).ConfigureAwait(false);
                EnsureResponseSucceeded(response);
                _v3Report = response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new System.TimeoutException("SNMP SET 请求超时。");
            }
        }

        private async ValueTask<ISnmpMessage> GetV3ReportAsync(SnmpType requestType, CancellationToken cancellationToken)
        {
            if (_v3Report != null)
                return _v3Report;
            Discovery discovery = Messenger.GetNextDiscovery(requestType);
            _v3Report = await discovery.GetResponseAsync(_endpoint, cancellationToken).ConfigureAwait(false);
            return _v3Report;
        }

        private IPrivacyProvider BuildPrivacyProvider()
        {
#pragma warning disable CS0618 // Legacy devices still require MD5/SHA1/DES/3DES compatibility.
            IAuthenticationProvider authentication;
            switch (_options.AuthenticationProtocol.Trim().ToUpperInvariant())
            {
                case "MD5": authentication = new MD5AuthenticationProvider(new OctetString(_options.AuthenticationPassword)); break;
                case "SHA":
                case "SHA1": authentication = new SHA1AuthenticationProvider(new OctetString(_options.AuthenticationPassword)); break;
                case "SHA256": authentication = new SHA256AuthenticationProvider(new OctetString(_options.AuthenticationPassword)); break;
                case "SHA384": authentication = new SHA384AuthenticationProvider(new OctetString(_options.AuthenticationPassword)); break;
                case "SHA512": authentication = new SHA512AuthenticationProvider(new OctetString(_options.AuthenticationPassword)); break;
                case "NONE": authentication = DefaultAuthenticationProvider.Instance; break;
                default: throw new InvalidOperationException("不支持的 SNMPv3 认证算法。");
            }

            OctetString privacyPassword = new OctetString(_options.PrivacyPassword);
            switch (_options.PrivacyProtocol.Trim().ToUpperInvariant())
            {
                case "NONE": return new DefaultPrivacyProvider(authentication);
                case "DES": return new DESPrivacyProvider(privacyPassword, authentication);
                case "3DES": return new TripleDESPrivacyProvider(privacyPassword, authentication);
                case "AES":
                case "AES128": return new AESPrivacyProvider(privacyPassword, authentication);
                case "AES192": return new AES192PrivacyProvider(privacyPassword, authentication);
                case "AES256": return new AES256PrivacyProvider(privacyPassword, authentication);
                default: throw new InvalidOperationException("不支持的 SNMPv3 隐私算法。");
            }
#pragma warning restore CS0618
        }

        private VersionCode GetVersion()
        {
            switch (_options.Version.Trim().ToUpperInvariant())
            {
                case "V1": return VersionCode.V1;
                case "V3": return VersionCode.V3;
                default: return VersionCode.V2;
            }
        }

        private PlcReadResult Decode(Variable variable, PlcDataType dataType)
            => new PlcReadResult(0, dataType.ToString(), SnmpDataCodec.Decode(variable.Data, dataType));

        private static void EnsureResponseSucceeded(ISnmpMessage response)
        {
            int status = response.Pdu().ErrorStatus.ToInt32();
            if (status != 0)
                throw new SnmpTagException("SNMP 响应错误：" + response.Pdu().ErrorStatus + "，索引 " + response.Pdu().ErrorIndex + "。");
        }

        private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
        {
            CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(Math.Max(100, _connection.TimeoutMilliseconds));
            return source;
        }

        private void EnsureConnected()
        {
            if (!_connected || _endpoint == null)
                throw new PlcCommunicationException("SNMP 设备尚未通过在线探测。");
        }

        private static bool IsTagError(Exception exception)
        {
            return exception is SnmpTagException || exception is FormatException || exception is OverflowException || exception is NotSupportedException;
        }
    }
}
