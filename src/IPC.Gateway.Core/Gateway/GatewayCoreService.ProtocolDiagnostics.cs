using System;
using System.Linq;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Core.Gateway;

public sealed partial class GatewayCoreService
{
    public static bool IsExclusiveDiagnosticBus(PlcProtocol protocol) => protocol is
        PlcProtocol.ModbusRtu or PlcProtocol.ModbusAscii or PlcProtocol.MitsubishiSerial or
        PlcProtocol.MitsubishiQlSerial or PlcProtocol.CanOpen;

    public string? CheckDiagnosticBusAvailability(PlcConnectionOptions connection)
    {
        if (!IsExclusiveDiagnosticBus(connection.Protocol)) return null;
        string target = NormalizeBusName(connection.Host);
        lock (_syncRoot)
        {
            if (!IsRunning) return null;
            foreach (var device in _project.Devices)
            {
                if (!device.Enabled || device.Connection == null || !IsExclusiveDiagnosticBus(device.Protocol)) continue;
                if (string.Equals(NormalizeBusName(device.Connection.Host), target, StringComparison.OrdinalIgnoreCase) &&
                    !_project.Channels.Any(channel => channel.Id == device.ChannelId && !channel.Enabled))
                    return "An enabled acquisition device is using this bus.";
            }
        }
        return null;
    }

    private static string NormalizeBusName(string? host)
    {
        var name = host?.Trim() ?? string.Empty;
        if (name.StartsWith(@"\\.\", StringComparison.Ordinal)) name = name[4..];
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "::1" ? "127.0.0.1" : name;
    }
}
