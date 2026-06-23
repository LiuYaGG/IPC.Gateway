/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：SimpleMqttClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    internal sealed class SimpleMqttClient : IDisposable
    {
        private readonly MqttGatewayOptions _options;
        private readonly object _sendSync;
        private TcpClient? _tcpClient;
        private Stream? _stream;
        private ushort _packetId;
        private bool _disposed;

        public SimpleMqttClient(MqttGatewayOptions options)
        {
            _options = options == null ? new MqttGatewayOptions() : options.Clone();
            _sendSync = new object();
            _packetId = 1;
        }

        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<MqttMessageEventArgs>? MessageReceived;

        public bool IsConnected
        {
            get { return _tcpClient != null && _tcpClient.Connected && _stream != null; }
        }

        public void ConnectAndReadLoop(string subscribeTopic, ManualResetEvent stopEvent)
        {
            ConnectAndReadLoop(subscribeTopic, stopEvent, null);
        }

        public void ConnectAndReadLoop(string subscribeTopic, ManualResetEvent stopEvent, Action<SimpleMqttClient>? idleAction)
        {
            ConnectAndReadLoop(subscribeTopic, stopEvent, idleAction, null);
        }

        public void ConnectAndReadLoop(string subscribeTopic, ManualResetEvent stopEvent, Action<SimpleMqttClient>? idleAction, MqttWillMessage? willMessage)
        {
            if (stopEvent == null)
                throw new ArgumentNullException("stopEvent");

            Connect(willMessage);
            RaiseConnected();
            Subscribe(subscribeTopic);
            if (idleAction != null && !stopEvent.WaitOne(0))
                idleAction(this);
            ReadLoop(stopEvent, idleAction);
        }

        public void Disconnect()
        {
            try
            {
                if (_stream != null)
                    SendPacket(0xE0, new byte[0]);
            }
            catch
            {
            }

            CloseSocket();
            RaiseDisconnected();
        }

        public MqttPublishResult Publish(string topic, string payload, int qos, int ackTimeoutMilliseconds)
        {
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload == null ? string.Empty : payload);
            return Publish(topic, payloadBytes, qos, ackTimeoutMilliseconds);
        }

        public MqttPublishResult Publish(string topic, byte[] payloadBytes, int qos, int ackTimeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("MQTT publish topic is empty.", "topic");

            int publishQos = MqttGatewayOptions.ClampQos(qos);
            MemoryStream packet = new MemoryStream();
            WriteUtf8(packet, topic.Trim());
            ushort packetId = 0;
            if (publishQos > 0)
            {
                packetId = NextPacketId();
                WriteUInt16(packet, packetId);
            }

            payloadBytes ??= Array.Empty<byte>();
            packet.Write(payloadBytes, 0, payloadBytes.Length);

            byte header = (byte)(0x30 | (publishQos << 1));
            SendPacket(header, packet.ToArray());
            if (publishQos == 0)
                return MqttPublishResult.Ok(0);

            int timeout = ackTimeoutMilliseconds <= 0 ? 5000 : ackTimeoutMilliseconds;
            return publishQos == 2 ? WaitForPubComplete(packetId, timeout) : WaitForPubAck(packetId, timeout);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Disconnect();
        }

        public void Connect()
        {
            Connect(null);
        }

        public void Connect(MqttWillMessage? willMessage)
        {
            string host = string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host.Trim();
            int port = MqttGatewayOptions.ClampPort(_options.Port);

            _tcpClient = new TcpClient();
            IAsyncResult async = _tcpClient.BeginConnect(host, port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(5000))
                throw new TimeoutException("MQTT broker connect timed out.");

            _tcpClient.EndConnect(async);
            _tcpClient.ReceiveTimeout = 1000;
            _tcpClient.SendTimeout = 5000;
            Stream transportStream = _tcpClient.GetStream();
            _stream = _options.UseTls ? CreateTlsStream(transportStream, host) : transportStream;

            MemoryStream payload = new MemoryStream();
            WriteUtf8(payload, string.IsNullOrWhiteSpace(_options.ClientId) ? BuildDefaultClientId() : _options.ClientId.Trim());
            bool hasWill = willMessage != null && !string.IsNullOrWhiteSpace(willMessage.Topic);
            if (hasWill)
            {
                WriteUtf8(payload, willMessage!.Topic.Trim());
                WriteBinary(payload, willMessage.Payload);
            }
            bool hasUsername = !string.IsNullOrWhiteSpace(_options.Username);
            bool hasPassword = !string.IsNullOrEmpty(_options.Password);
            if (hasUsername)
                WriteUtf8(payload, _options.Username);
            if (hasPassword)
                WriteUtf8(payload, _options.Password);

            MemoryStream variable = new MemoryStream();
            WriteUtf8(variable, "MQTT");
            variable.WriteByte(4);
            byte flags = 0x02;
            if (hasWill)
            {
                flags |= 0x04;
                flags |= (byte)(MqttGatewayOptions.ClampQos(willMessage!.Qos) << 3);
                if (willMessage.Retain)
                    flags |= 0x20;
            }
            if (hasUsername)
                flags |= 0x80;
            if (hasPassword)
                flags |= 0x40;
            variable.WriteByte(flags);
            WriteUInt16(variable, (ushort)MqttGatewayOptions.ClampKeepAliveSeconds(_options.KeepAliveSeconds));
            byte[] payloadBytes = payload.ToArray();
            variable.Write(payloadBytes, 0, payloadBytes.Length);

            SendPacket(0x10, variable.ToArray());
            MqttPacket? packet = ReadPacket();
            if (packet == null || packet.Type != 2 || packet.Payload.Length < 2)
                throw new InvalidOperationException("MQTT broker did not return CONNACK.");
            if (packet.Payload[1] != 0)
                throw new InvalidOperationException("MQTT broker rejected connection. Return code: " + packet.Payload[1]);
        }

        private void Subscribe(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new InvalidOperationException("MQTT subscribe topic is empty.");

            MemoryStream payload = new MemoryStream();
            WriteUInt16(payload, NextPacketId());
            WriteUtf8(payload, topic.Trim());
            payload.WriteByte(0);

            SendPacket(0x82, payload.ToArray());
            MqttPacket? packet = ReadPacket();
            if (packet == null || packet.Type != 9)
                throw new InvalidOperationException("MQTT broker did not return SUBACK.");
        }

        private void ReadLoop(ManualResetEvent stopEvent, Action<SimpleMqttClient>? idleAction)
        {
            DateTime nextPingUtc = DateTime.UtcNow.AddSeconds(Math.Max(5, MqttGatewayOptions.ClampKeepAliveSeconds(_options.KeepAliveSeconds) / 2));
            while (!stopEvent.WaitOne(0))
            {
                try
                {
                    if (DateTime.UtcNow >= nextPingUtc)
                    {
                        SendPacket(0xC0, new byte[0]);
                        nextPingUtc = DateTime.UtcNow.AddSeconds(Math.Max(5, MqttGatewayOptions.ClampKeepAliveSeconds(_options.KeepAliveSeconds) / 2));
                    }

                    MqttPacket? packet = ReadPacket();
                    if (packet == null)
                    {
                        if (idleAction != null)
                            idleAction(this);
                        continue;
                    }

                    if (packet.Type == 3)
                        HandlePublish(packet);
                    else if (packet.Type == 4)
                        continue;
                    else if (packet.Type == 13)
                        continue;
                }
                catch (IOException)
                {
                    if (!stopEvent.WaitOne(0))
                        throw;
                }
            }
        }

        private void HandlePublish(MqttPacket packet)
        {
            if (packet.Payload.Length < 2)
                return;

            int offset = 0;
            string topic = ReadUtf8(packet.Payload, ref offset);
            int qos = (packet.Flags >> 1) & 0x03;
            ushort packetId = 0;
            if (qos > 0)
            {
                if (packet.Payload.Length < offset + 2)
                    return;
                packetId = ReadUInt16(packet.Payload, offset);
                offset += 2;
            }

            string payload = Encoding.UTF8.GetString(packet.Payload, offset, packet.Payload.Length - offset);
            EventHandler<MqttMessageEventArgs>? handler = MessageReceived;
            if (handler != null)
                handler(this, new MqttMessageEventArgs(topic, payload));

            if (qos == 1)
            {
                MemoryStream ack = new MemoryStream();
                WriteUInt16(ack, packetId);
                SendPacket(0x40, ack.ToArray());
            }
        }

        private MqttPublishResult WaitForPubAck(ushort expectedPacketId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                MqttPacket? packet = ReadPacket();
                if (packet == null)
                    continue;

                if (packet.Type == 4 && packet.Payload.Length >= 2)
                {
                    ushort packetId = ReadUInt16(packet.Payload, 0);
                    if (packetId == expectedPacketId)
                        return MqttPublishResult.Ok(packetId);
                }
                else if (packet.Type == 3)
                {
                    HandlePublish(packet);
                }
                else if (packet.Type == 13)
                {
                    continue;
                }

            }

            return MqttPublishResult.Fail("PUBACK timed out for packet " + expectedPacketId + ".");
        }

        private MqttPublishResult WaitForPubComplete(ushort expectedPacketId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            bool pubRelSent = false;
            while (DateTime.UtcNow < deadline)
            {
                MqttPacket? packet = ReadPacket();
                if (packet == null)
                    continue;

                if (packet.Type == 5 && packet.Payload.Length >= 2)
                {
                    ushort packetId = ReadUInt16(packet.Payload, 0);
                    if (packetId == expectedPacketId)
                    {
                        MemoryStream pubRel = new MemoryStream();
                        WriteUInt16(pubRel, packetId);
                        SendPacket(0x62, pubRel.ToArray());
                        pubRelSent = true;
                    }
                }
                else if (packet.Type == 7 && packet.Payload.Length >= 2)
                {
                    ushort packetId = ReadUInt16(packet.Payload, 0);
                    if (packetId == expectedPacketId)
                        return MqttPublishResult.Ok(packetId);
                }
                else if (packet.Type == 3)
                {
                    HandlePublish(packet);
                }
                else if (packet.Type == 13)
                {
                    continue;
                }
            }

            return MqttPublishResult.Fail((pubRelSent ? "PUBCOMP" : "PUBREC") + " timed out for packet " + expectedPacketId + ".");
        }

        private MqttPacket? ReadPacket()
        {
            Stream stream = _stream ?? throw new IOException("MQTT stream is not connected.");
            int first;
            try
            {
                first = stream.ReadByte();
            }
            catch (IOException)
            {
                return null;
            }

            if (first < 0)
                throw new IOException("MQTT connection was closed.");

            int remainingLength = ReadRemainingLength(stream);
            byte[] payload = ReadExact(stream, remainingLength);
            return new MqttPacket((byte)(first >> 4), (byte)(first & 0x0F), payload);
        }

        private void SendPacket(byte header, byte[] payload)
        {
            Stream? stream = _stream;
            if (stream == null)
                throw new IOException("MQTT stream is not connected.");

            lock (_sendSync)
            {
                stream.WriteByte(header);
                WriteRemainingLength(stream, payload == null ? 0 : payload.Length);
                if (payload != null && payload.Length > 0)
                    stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
        }

        private void CloseSocket()
        {
            try
            {
                if (_stream != null)
                    _stream.Dispose();
            }
            catch
            {
            }

            try
            {
                if (_tcpClient != null)
                    _tcpClient.Close();
            }
            catch
            {
            }

            _stream = null;
            _tcpClient = null;
        }

        private ushort NextPacketId()
        {
            if (_packetId == ushort.MaxValue)
                _packetId = 1;
            return _packetId++;
        }

        private static string BuildDefaultClientId()
        {
            return "IPC-Gateway-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private Stream CreateTlsStream(Stream transportStream, string host)
        {
            SslStream sslStream = new SslStream(
                transportStream,
                leaveInnerStreamOpen: false,
                ValidateServerCertificate);

            X509CertificateCollection clientCertificates = LoadClientCertificates();
            sslStream.AuthenticateAsClient(
                host,
                clientCertificates,
                SslProtocols.Tls12 | SslProtocols.Tls13,
                checkCertificateRevocation: true);
            return sslStream;
        }

        private X509CertificateCollection LoadClientCertificates()
        {
            X509CertificateCollection certificates = new X509CertificateCollection();
            string path = ResolvePath(_options.ClientCertificatePath);
            if (string.IsNullOrWhiteSpace(path))
                return certificates;
            if (!File.Exists(path))
                throw new FileNotFoundException("MQTT client certificate file was not found.", path);

            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                _options.ClientCertificatePassword ?? string.Empty,
                X509KeyStorageFlags.EphemeralKeySet);
            certificates.Add(certificate);
            return certificates;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (_options.AllowUntrustedCertificates)
                return true;

            string expectedThumbprint = NormalizeThumbprint(_options.ServerCertificateThumbprint);
            if (!string.IsNullOrWhiteSpace(expectedThumbprint) && certificate != null)
            {
                using X509Certificate2 actual = new X509Certificate2(certificate);
                return NormalizeThumbprint(actual.Thumbprint).Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(_options.CaCertificatePath) && certificate != null)
            {
                using X509Certificate2 actual = new X509Certificate2(certificate);
                return ValidateWithCustomCa(actual);
            }

            return errors == SslPolicyErrors.None;
        }

        private bool ValidateWithCustomCa(X509Certificate2 certificate)
        {
            X509Certificate2Collection roots = LoadCaCertificates(_options.CaCertificatePath);
            if (roots.Count == 0)
                return false;

            using X509Chain customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (X509Certificate2 root in roots)
                customChain.ChainPolicy.CustomTrustStore.Add(root);

            return customChain.Build(certificate);
        }

        private static X509Certificate2Collection LoadCaCertificates(string path)
        {
            X509Certificate2Collection certificates = new X509Certificate2Collection();
            string resolved = ResolvePath(path);
            if (string.IsNullOrWhiteSpace(resolved))
                return certificates;

            IEnumerable<string> files = Directory.Exists(resolved)
                ? Directory.EnumerateFiles(resolved, "*.*", SearchOption.TopDirectoryOnly)
                : File.Exists(resolved) ? new[] { resolved } : Array.Empty<string>();

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".cer" && extension != ".crt" && extension != ".der" && extension != ".pem")
                    continue;

                try
                {
                    certificates.Add(X509CertificateLoader.LoadCertificateFromFile(file));
                }
                catch
                {
                }
            }

            return certificates;
        }

        private static string ResolvePath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
        }

        private static string NormalizeThumbprint(string? value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("MQTT connection was closed.");
                offset += read;
            }
            return buffer;
        }

        private static int ReadRemainingLength(Stream stream)
        {
            int multiplier = 1;
            int value = 0;
            int digit;
            do
            {
                digit = stream.ReadByte();
                if (digit < 0)
                    throw new IOException("MQTT connection was closed.");
                value += (digit & 127) * multiplier;
                multiplier *= 128;
                if (multiplier > 128 * 128 * 128 * 128)
                    throw new InvalidOperationException("MQTT remaining length is invalid.");
            }
            while ((digit & 128) != 0);
            return value;
        }

        private static void WriteRemainingLength(Stream stream, int value)
        {
            do
            {
                int digit = value % 128;
                value = value / 128;
                if (value > 0)
                    digit = digit | 128;
                stream.WriteByte((byte)digit);
            }
            while (value > 0);
        }

        private static void WriteUtf8(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value == null ? string.Empty : value);
            WriteBinary(stream, bytes);
        }

        private static void WriteBinary(Stream stream, byte[]? bytes)
        {
            bytes ??= Array.Empty<byte>();
            WriteUInt16(stream, (ushort)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadUtf8(byte[] data, ref int offset)
        {
            if (data.Length < offset + 2)
                return string.Empty;
            ushort length = ReadUInt16(data, offset);
            offset += 2;
            if (data.Length < offset + length)
                return string.Empty;
            string value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return value;
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private void RaiseConnected()
        {
            EventHandler? handler = Connected;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void RaiseDisconnected()
        {
            EventHandler? handler = Disconnected;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        
        
        
        
        
        
        
        
        
        private sealed class MqttPacket
        {
            public MqttPacket(byte type, byte flags, byte[] payload)
            {
                Type = type;
                Flags = flags;
                Payload = payload ?? new byte[0];
            }

            public byte Type { get; private set; }
            public byte Flags { get; private set; }
            public byte[] Payload { get; private set; }
        }
    }

    
    
    
    
    
    
    
    
    
    internal sealed class MqttMessageEventArgs : EventArgs
    {
        public MqttMessageEventArgs(string topic, string payload)
        {
            Topic = topic ?? string.Empty;
            Payload = payload ?? string.Empty;
        }

        public string Topic { get; private set; }
        public string Payload { get; private set; }
    }

    internal sealed class MqttWillMessage
    {
        public MqttWillMessage(string topic, byte[] payload, int qos, bool retain)
        {
            Topic = topic ?? string.Empty;
            Payload = payload ?? Array.Empty<byte>();
            Qos = MqttGatewayOptions.ClampQos(qos);
            Retain = retain;
        }

        public string Topic { get; private set; }
        public byte[] Payload { get; private set; }
        public int Qos { get; private set; }
        public bool Retain { get; private set; }
    }
}
