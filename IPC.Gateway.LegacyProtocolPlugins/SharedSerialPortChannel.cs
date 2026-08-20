#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    internal sealed class SharedSerialPortLease : IDisposable
    {
        private SharedSerialPortChannel? _channel;

        internal SharedSerialPortLease(SharedSerialPortChannel channel)
        {
            _channel = channel;
        }

        public SerialPort Port => GetChannel().Port;
        public object SyncRoot => GetChannel().SyncRoot;
        public bool IsOpen => _channel?.Port.IsOpen == true;

        public void Dispose()
        {
            SharedSerialPortChannel? channel = Interlocked.Exchange(ref _channel, null);
            if (channel != null)
                SharedSerialPortRegistry.Release(channel);
        }

        private SharedSerialPortChannel GetChannel()
        {
            return _channel ?? throw new ObjectDisposedException(nameof(SharedSerialPortLease));
        }
    }

    internal sealed class SharedSerialPortChannel : IDisposable
    {
        public SharedSerialPortChannel(string key, PlcProtocol protocol, PlcConnectionOptions options, int defaultDataBits)
        {
            Key = key;
            Protocol = protocol;
            int timeout = options.TimeoutMilliseconds > 0 ? options.TimeoutMilliseconds : 3000;
            Port = new SerialPort(
                string.IsNullOrWhiteSpace(options.Host) ? "COM1" : options.Host.Trim(),
                options.Port > 0 ? options.Port : 9600,
                SerialPortOptionMapper.MapParity(options.SerialParity),
                options.DataBits > 0 ? options.DataBits : defaultDataBits,
                SerialPortOptionMapper.MapStopBits(options.SerialStopBits))
            {
                ReadTimeout = timeout,
                WriteTimeout = timeout
            };
            Port.Open();
        }

        public string Key { get; }
        public PlcProtocol Protocol { get; }
        public SerialPort Port { get; }
        public object SyncRoot { get; } = new object();
        public int ReferenceCount { get; set; }

        public void Dispose()
        {
            if (Port.IsOpen)
                Port.Close();
            Port.Dispose();
        }
    }

    internal static class SharedSerialPortRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, SharedSerialPortChannel> Channels =
            new Dictionary<string, SharedSerialPortChannel>(StringComparer.OrdinalIgnoreCase);

        public static SharedSerialPortLease Acquire(
            PlcConnectionOptions options,
            PlcProtocol protocol,
            int defaultDataBits)
        {
            string key = BuildKey(options, defaultDataBits);
            lock (SyncRoot)
            {
                if (!Channels.TryGetValue(key, out SharedSerialPortChannel? channel))
                {
                    channel = new SharedSerialPortChannel(key, protocol, options, defaultDataBits);
                    Channels.Add(key, channel);
                }
                else if (channel.Protocol != protocol)
                {
                    throw new InvalidOperationException(
                        "同一串口不能同时配置不同的三菱串口协议。请统一通道协议后重试。");
                }

                channel.ReferenceCount++;
                return new SharedSerialPortLease(channel);
            }
        }

        public static void Release(SharedSerialPortChannel channel)
        {
            lock (SyncRoot)
            {
                if (--channel.ReferenceCount > 0)
                    return;
                Channels.Remove(channel.Key);
                channel.Dispose();
            }
        }

        private static string BuildKey(PlcConnectionOptions options, int defaultDataBits)
        {
            return string.Join("|", new[]
            {
                (options.Host ?? "COM1").Trim().ToUpperInvariant(),
                (options.Port > 0 ? options.Port : 9600).ToString(CultureInfo.InvariantCulture),
                (options.DataBits > 0 ? options.DataBits : defaultDataBits).ToString(CultureInfo.InvariantCulture),
                options.SerialParity.ToString(),
                options.SerialStopBits.ToString()
            });
        }
    }
}
