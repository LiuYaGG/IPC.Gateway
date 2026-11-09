#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;
using NModbus;
using NModbus.Serial;

namespace IPC.Plc.Communication.ModbusRtu
{
    internal sealed class SharedModbusSerialChannelLease : IDisposable
    {
        private SharedModbusSerialChannel? _channel;

        internal SharedModbusSerialChannelLease(SharedModbusSerialChannel channel)
        {
            _channel = channel;
        }

        public bool IsOpen => _channel?.IsOpen == true;

        public NModbusMasterAdapter CreateAdapter(byte unitId)
        {
            SharedModbusSerialChannel channel = _channel
                ?? throw new ObjectDisposedException(nameof(SharedModbusSerialChannelLease));
            return channel.CreateAdapter(unitId);
        }

        public void Dispose()
        {
            SharedModbusSerialChannel? channel = Interlocked.Exchange(ref _channel, null);
            if (channel != null)
                SharedModbusSerialChannelRegistry.Release(channel);
        }
    }

    internal sealed class SharedModbusSerialChannel : IDisposable
    {
        private readonly SerialPort _port;
        private readonly IModbusMaster _master;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        public SharedModbusSerialChannel(string key, PlcConnectionOptions options, PlcProtocol protocol)
        {
            Key = key;
            TimeoutMilliseconds = options.TimeoutMilliseconds > 0 ? options.TimeoutMilliseconds : 3000;
            int baudRate = options.Port > 0 ? options.Port : 9600;
            int dataBits = options.DataBits > 0 ? options.DataBits : 8;
            _port = new SerialPort(
                string.IsNullOrWhiteSpace(options.Host) ? "COM1" : options.Host.Trim(),
                baudRate,
                IPC.Gateway.LegacyProtocolPlugins.SerialPortOptionMapper.MapParity(options.SerialParity),
                dataBits,
                IPC.Gateway.LegacyProtocolPlugins.SerialPortOptionMapper.MapStopBits(options.SerialStopBits))
            {
                ReadTimeout = TimeoutMilliseconds,
                WriteTimeout = TimeoutMilliseconds
            };

            try
            {
                _port.Open();
                SerialPortAdapter resource = new SerialPortAdapter(_port);
                ModbusFactory factory = new ModbusFactory();
                _master = protocol == PlcProtocol.ModbusAscii
                    ? factory.CreateAsciiMaster(resource)
                    : factory.CreateRtuMaster(resource);
                _master.Transport.ReadTimeout = TimeoutMilliseconds;
                _master.Transport.WriteTimeout = TimeoutMilliseconds;
                _master.Transport.Retries = 0;
            }
            catch
            {
                _port.Dispose();
                throw;
            }
        }

        public string Key { get; }
        public int TimeoutMilliseconds { get; }
        public int ReferenceCount { get; set; }
        public bool IsOpen => _port.IsOpen;

        public NModbusMasterAdapter CreateAdapter(byte unitId)
        {
            return NModbusMasterAdapter.CreateShared(
                _master,
                unitId,
                TimeoutMilliseconds,
                _operationLock);
        }

        public void Dispose()
        {
            _master.Dispose();
            if (_port.IsOpen)
                _port.Close();
            _port.Dispose();
            _operationLock.Dispose();
        }
    }

    internal static class SharedModbusSerialChannelRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, SharedModbusSerialChannel> Channels =
            new Dictionary<string, SharedModbusSerialChannel>(StringComparer.OrdinalIgnoreCase);

        public static SharedModbusSerialChannelLease Acquire(PlcConnectionOptions options, PlcProtocol protocol)
        {
            string key = BuildKey(options, protocol);
            lock (SyncRoot)
            {
                if (!Channels.TryGetValue(key, out SharedModbusSerialChannel? channel))
                {
                    channel = new SharedModbusSerialChannel(key, options, protocol);
                    Channels.Add(key, channel);
                }

                channel.ReferenceCount++;
                return new SharedModbusSerialChannelLease(channel);
            }
        }

        public static void Release(SharedModbusSerialChannel channel)
        {
            lock (SyncRoot)
            {
                if (--channel.ReferenceCount > 0)
                    return;

                Channels.Remove(channel.Key);
                channel.Dispose();
            }
        }

        private static string BuildKey(PlcConnectionOptions options, PlcProtocol protocol)
        {
            return string.Join("|", new[]
            {
                protocol.ToString(),
                (options.Host ?? "COM1").Trim().ToUpperInvariant(),
                (options.Port > 0 ? options.Port : 9600).ToString(CultureInfo.InvariantCulture),
                (options.DataBits > 0 ? options.DataBits : 8).ToString(CultureInfo.InvariantCulture),
                options.SerialParity.ToString(),
                options.SerialStopBits.ToString()
            });
        }
    }
}
