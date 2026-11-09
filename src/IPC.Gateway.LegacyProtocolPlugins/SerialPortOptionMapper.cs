/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.LegacyProtocolPlugins
* 项目描述 ：
* 类 名 称 ：SerialPortOptionMapper
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.LegacyProtocolPlugins
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
using GatewayParity = IPC.Plc.Communication.Core.Parity;
using GatewayStopBits = IPC.Plc.Communication.Core.StopBits;
using PortsParity = System.IO.Ports.Parity;
using PortsStopBits = System.IO.Ports.StopBits;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    internal static class SerialPortOptionMapper
    {
        public static PortsParity MapParity(GatewayParity value)
        {
            switch (value)
            {
                case GatewayParity.Odd:
                    return PortsParity.Odd;
                case GatewayParity.Even:
                    return PortsParity.Even;
                case GatewayParity.Mark:
                    return PortsParity.Mark;
                case GatewayParity.Space:
                    return PortsParity.Space;
                default:
                    return PortsParity.None;
            }
        }

        public static PortsStopBits MapStopBits(GatewayStopBits value)
        {
            switch (value)
            {
                case GatewayStopBits.None:
                    return PortsStopBits.None;
                case GatewayStopBits.Two:
                    return PortsStopBits.Two;
                case GatewayStopBits.OnePointFive:
                    return PortsStopBits.OnePointFive;
                default:
                    return PortsStopBits.One;
            }
        }
    }
}
