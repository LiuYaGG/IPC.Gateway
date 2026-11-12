using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Plc.Communication.ModbusUdp;

/// <summary>Modbus IP ADU (MBAP header + PDU) carried by one UDP datagram.</summary>
public sealed class ModbusUdpClient : ModbusTcpClient
{
    public ModbusUdpClient(PlcConnectionOptions options) : base(options, PlcProtocol.ModbusUdp) { }
}
