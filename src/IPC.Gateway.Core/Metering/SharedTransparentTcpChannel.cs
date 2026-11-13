using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering
{
    internal sealed class SharedTransparentTcpLease : IDisposable
    {
        private SharedTransparentTcpChannel? _channel;

        internal SharedTransparentTcpLease(SharedTransparentTcpChannel channel)
        {
            _channel = channel;
        }

        public bool IsConnected => _channel?.IsConnected == true;
        public NetworkStream Stream => GetChannel().Stream;
        public void MarkTransportFailure() => _channel?.MarkTransportFailure();
        public byte NextSequence() => GetChannel().NextSequence();

        public IDisposable Enter()
        {
            SharedTransparentTcpChannel channel = GetChannel();
            channel.OperationLock.Wait();
            return new OperationLease(channel.OperationLock);
        }

        public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            SharedTransparentTcpChannel channel = GetChannel();
            await channel.OperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new OperationLease(channel.OperationLock);
        }

        public void Dispose()
        {
            SharedTransparentTcpChannel? channel = Interlocked.Exchange(ref _channel, null);
            if (channel != null)
                SharedTransparentTcpChannelRegistry.Release(channel);
        }

        private SharedTransparentTcpChannel GetChannel()
        {
            return _channel ?? throw new ObjectDisposedException(nameof(SharedTransparentTcpLease));
        }

        private sealed class OperationLease : IDisposable
        {
            private SemaphoreSlim? _gate;

            public OperationLease(SemaphoreSlim gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _gate, null)?.Release();
            }
        }
    }

    internal sealed class SharedTransparentTcpChannel : IDisposable
    {
        private SharedTransparentTcpChannel(string key, TcpClient client, NetworkStream stream)
        {
            Key = key;
            Client = client;
            Stream = stream;
        }

        public string Key { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public SemaphoreSlim OperationLock { get; } = new SemaphoreSlim(1, 1);
        private int _sequence = -1;
        public byte NextSequence() => unchecked((byte)Interlocked.Increment(ref _sequence));
        public int ReferenceCount { get; set; }
        private volatile bool _transportFailed;
        public bool IsConnected => !_transportFailed && Client.Connected;
        public void MarkTransportFailure() => _transportFailed = true;

        public static async ValueTask<SharedTransparentTcpChannel> ConnectAsync(
            string key,
            PlcConnectionOptions options,
            CancellationToken cancellationToken)
        {
            int timeout = options.TimeoutMilliseconds > 0 ? options.TimeoutMilliseconds : 3000;
            int port = options.Port > 0 ? options.Port : 4001;
            TcpClient client = new TcpClient
            {
                ReceiveTimeout = timeout,
                SendTimeout = timeout
            };
            try
            {
                await client.ConnectAsync(options.Host, port, cancellationToken).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = timeout;
                stream.WriteTimeout = timeout;
                return new SharedTransparentTcpChannel(key, client, stream);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Stream.Dispose();
            Client.Dispose();
            OperationLock.Dispose();
        }
    }

    internal static class SharedTransparentTcpChannelRegistry
    {
        private static readonly object RegistryLock = new();
        private sealed class EndpointGate
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public int Users;
        }
        private static readonly Dictionary<string, EndpointGate> Connecting = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SharedTransparentTcpChannel> Channels =
            new Dictionary<string, SharedTransparentTcpChannel>(StringComparer.OrdinalIgnoreCase);

        public static SharedTransparentTcpLease Acquire(PlcConnectionOptions options)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(
                options.TimeoutMilliseconds > 0 ? options.TimeoutMilliseconds : 3000);
            return AcquireAsync(options, timeout.Token).AsTask().GetAwaiter().GetResult();
        }

        public static async ValueTask<SharedTransparentTcpLease> AcquireAsync(
            PlcConnectionOptions options,
            CancellationToken cancellationToken)
        {
            string key = BuildKey(options);
            EndpointGate endpoint;
            lock (RegistryLock)
            {
                if (TryAcquireExisting(key) is { } existing) return existing;
                if (!Connecting.TryGetValue(key, out endpoint!))
                    Connecting.Add(key, endpoint = new EndpointGate());
                endpoint.Users++;
            }
            try
            {
                await endpoint.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    lock (RegistryLock)
                        if (TryAcquireExisting(key) is { } existing) return existing;
                    var channel = await SharedTransparentTcpChannel.ConnectAsync(key, options, cancellationToken).ConfigureAwait(false);
                    lock (RegistryLock)
                    {
                        channel.ReferenceCount = 1;
                        Channels[key] = channel;
                    }
                    return new SharedTransparentTcpLease(channel);
                }
                finally { endpoint.Gate.Release(); }
            }
            finally
            {
                lock (RegistryLock)
                    if (--endpoint.Users == 0)
                    {
                        Connecting.Remove(key);
                        endpoint.Gate.Dispose();
                    }
            }
        }

        private static SharedTransparentTcpLease? TryAcquireExisting(string key)
        {
            if (!Channels.TryGetValue(key, out var channel) || !channel.IsConnected) return null;
            channel.ReferenceCount++;
            return new SharedTransparentTcpLease(channel);
        }

        public static void Release(SharedTransparentTcpChannel channel)
        {
            lock (RegistryLock)
            {
                if (--channel.ReferenceCount > 0) return;
                if (Channels.TryGetValue(channel.Key, out var active) && ReferenceEquals(active, channel))
                    Channels.Remove(channel.Key);
            }
            channel.Dispose();
        }

        private static string BuildKey(PlcConnectionOptions options)
        {
            return (options.Host ?? string.Empty).Trim().ToUpperInvariant() + ":" +
                   (options.Port > 0 ? options.Port : 4001).ToString(CultureInfo.InvariantCulture);
        }
    }
}
