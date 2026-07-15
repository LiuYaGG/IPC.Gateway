#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO.BACnet;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Bacnet
{
    internal sealed class SharedBacnetIpChannelLease : IDisposable
    {
        private SharedBacnetIpChannel? _channel;

        internal SharedBacnetIpChannelLease(SharedBacnetIpChannel channel)
        {
            _channel = channel;
        }

        public BacnetClient Client => GetChannel().Client;
        public object SyncRoot => GetChannel().SyncRoot;
        public bool IsOpen => _channel != null;

        public void Dispose()
        {
            SharedBacnetIpChannel? channel = Interlocked.Exchange(ref _channel, null);
            if (channel != null)
                SharedBacnetIpChannelRegistry.Release(channel);
        }

        private SharedBacnetIpChannel GetChannel()
        {
            return _channel ?? throw new ObjectDisposedException(nameof(SharedBacnetIpChannelLease));
        }
    }

    internal sealed class SharedBacnetIpChannel : IDisposable
    {
        public SharedBacnetIpChannel(
            string key,
            PlcConnectionOptions options,
            int localPort,
            bool useExclusivePort,
            bool dontFragment,
            int maxPayload,
            string localEndpointIp,
            int retries,
            string bbmdAddress,
            int bbmdPort,
            int bbmdTtlSeconds)
        {
            Key = key;
            int timeout = options.TimeoutMilliseconds > 0 ? options.TimeoutMilliseconds : 3000;
            Client = StartClient(
                localPort,
                useExclusivePort,
                dontFragment,
                maxPayload,
                localEndpointIp,
                timeout,
                retries);
            try
            {
                if (!string.IsNullOrWhiteSpace(bbmdAddress))
                    Client.RegisterAsForeignDevice(bbmdAddress.Trim(), (short)bbmdPort, bbmdTtlSeconds);
            }
            catch
            {
                Client.Dispose();
                throw;
            }
        }

        public string Key { get; }
        public BacnetClient Client { get; }
        public object SyncRoot { get; } = new object();
        public int ReferenceCount { get; set; }

        public void Dispose()
        {
            Client.Dispose();
        }

        private static BacnetClient StartClient(
            int localPort,
            bool useExclusivePort,
            bool dontFragment,
            int maxPayload,
            string localEndpointIp,
            int timeout,
            int retries)
        {
            int attemptCount = localPort > 0 ? 1 : 5;
            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                int effectiveLocalPort = localPort > 0 ? localPort : ReserveEphemeralUdpPort();
                BacnetIpUdpProtocolTransport transport = new BacnetIpUdpProtocolTransport(
                    effectiveLocalPort,
                    useExclusivePort,
                    dontFragment,
                    maxPayload,
                    localEndpointIp);
                BacnetClient client = new BacnetClient(transport, timeout, retries);
                try
                {
                    client.Start();
                    return client;
                }
                catch (SocketException ex) when (
                    localPort <= 0 && ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    client.Dispose();
                    if (attempt + 1 >= attemptCount)
                        throw;
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException("BACnet/IP transport could not allocate a local UDP port.");
        }

        private static int ReserveEphemeralUdpPort()
        {
            using UdpClient reservation = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            return ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        }
    }

    internal static class SharedBacnetIpChannelRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, SharedBacnetIpChannel> Channels =
            new Dictionary<string, SharedBacnetIpChannel>(StringComparer.OrdinalIgnoreCase);

        public static SharedBacnetIpChannelLease Acquire(
            PlcConnectionOptions options,
            int localPort,
            bool useExclusivePort,
            bool dontFragment,
            int maxPayload,
            string localEndpointIp,
            int retries,
            string bbmdAddress,
            int bbmdPort,
            int bbmdTtlSeconds)
        {
            string key = BuildKey(localPort, localEndpointIp, useExclusivePort, dontFragment, maxPayload) + "|" +
                (bbmdAddress ?? string.Empty).Trim().ToUpperInvariant() + "|" + bbmdPort + "|" + bbmdTtlSeconds;
            lock (SyncRoot)
            {
                if (!Channels.TryGetValue(key, out SharedBacnetIpChannel? channel))
                {
                    channel = new SharedBacnetIpChannel(
                        key,
                        options,
                        localPort,
                        useExclusivePort,
                        dontFragment,
                        maxPayload,
                        localEndpointIp,
                        retries,
                        bbmdAddress ?? string.Empty,
                        bbmdPort,
                        bbmdTtlSeconds);
                    Channels.Add(key, channel);
                }
                channel.ReferenceCount++;
                return new SharedBacnetIpChannelLease(channel);
            }
        }

        public static void Release(SharedBacnetIpChannel channel)
        {
            lock (SyncRoot)
            {
                if (--channel.ReferenceCount > 0)
                    return;
                Channels.Remove(channel.Key);
                channel.Dispose();
            }
        }

        private static string BuildKey(
            int localPort,
            string localEndpointIp,
            bool useExclusivePort,
            bool dontFragment,
            int maxPayload)
        {
            return string.Join("|", new[]
            {
                localPort.ToString(CultureInfo.InvariantCulture),
                (localEndpointIp ?? string.Empty).Trim().ToUpperInvariant(),
                useExclusivePort.ToString(),
                dontFragment.ToString(),
                maxPayload.ToString(CultureInfo.InvariantCulture)
            });
        }
    }
}
