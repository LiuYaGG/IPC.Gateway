using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusRtu;

namespace IPC.Plc.Communication.ModbusAscii
{
    public sealed class ModbusAsciiClient : ModbusRtuClient
    {
        public ModbusAsciiClient(PlcConnectionOptions options)
            : base(options, PlcProtocol.ModbusAscii)
        {
        }
    }
}
