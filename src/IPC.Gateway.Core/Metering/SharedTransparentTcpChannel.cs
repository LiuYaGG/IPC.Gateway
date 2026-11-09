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
        public int ReferenceCount { get; set; }
        public bool IsConnected => Client.Connected;

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
        private static readonly SemaphoreSlim RegistryLock = new SemaphoreSlim(1, 1);
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
            await RegistryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!Channels.TryGetValue(key, out SharedTransparentTcpChannel? channel))
                {
                    channel = await SharedTransparentTcpChannel.ConnectAsync(key, options, cancellationToken).ConfigureAwait(false);
                    Channels.Add(key, channel);
                }
                channel.ReferenceCount++;
                return new SharedTransparentTcpLease(channel);
            }
            finally
            {
                RegistryLock.Release();
            }
        }

        public static void Release(SharedTransparentTcpChannel channel)
        {
            RegistryLock.Wait();
            try
            {
                if (--channel.ReferenceCount > 0)
                    return;
                Channels.Remove(channel.Key);
                channel.Dispose();
            }
            finally
            {
                RegistryLock.Release();
            }
        }

        private static string BuildKey(PlcConnectionOptions options)
        {
            return (options.Host ?? string.Empty).Trim().ToUpperInvariant() + ":" +
                   (options.Port > 0 ? options.Port : 4001).ToString(CultureInfo.InvariantCulture);
        }
    }
}
