/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.LegacyProtocolPlugins
* 项目描述 ：
* 类 名 称 ：RockwellCipProtocolDriver
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
using System;
using System.Collections.Generic;
using IPC.Plc.Communication.Bacnet;
using IPC.Plc.Communication.CanOpen;
using IPC.Plc.Communication.Cip;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;
using IPC.Plc.Communication.MitsubishiMc1E;
using IPC.Plc.Communication.MitsubishiQlSerial;
using IPC.Plc.Communication.MitsubishiSerial;
using IPC.Plc.Communication.ModbusRtu;
using IPC.Plc.Communication.OmronFins;
using IPC.Plc.Communication.OpcDa;
using IPC.Plc.Communication.OpcUa;
using IPC.Plc.Communication.SiemensS7;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    public sealed class RockwellCipProtocolDriver : LegacyProtocolDriver
    {
        public RockwellCipProtocolDriver()
            : base("legacy.rockwell-cip", "Rockwell CIP", PlcProtocol.RockwellCip)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new CipClient(options);
        }
    }

    public sealed class SiemensS7ProtocolDriver : LegacyProtocolDriver
    {
        public SiemensS7ProtocolDriver()
            : base("legacy.siemens-s7", "Siemens S7", PlcProtocol.SiemensS7)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new S7Client(options);
        }
    }

    public sealed class MitsubishiMcProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiMcProtocolDriver()
            : base("legacy.mitsubishi-mc", "Mitsubishi MC", PlcProtocol.MitsubishiMc)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new McClient(options);
        }
    }

    public sealed class MitsubishiMc1EProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiMc1EProtocolDriver()
            : base("legacy.mitsubishi-mc-1e", "Mitsubishi MC 1E", PlcProtocol.MitsubishiMc1E)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new Mc1EClient(options);
        }
    }

    public sealed class MitsubishiSerialProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiSerialProtocolDriver()
            : base("legacy.mitsubishi-serial", "Mitsubishi FX Serial", PlcProtocol.MitsubishiSerial)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new MitsubishiSerialClient(options);
        }
    }

    public sealed class MitsubishiQlSerialProtocolDriver : LegacyProtocolDriver
    {
        public MitsubishiQlSerialProtocolDriver()
            : base("legacy.mitsubishi-ql-serial", "Mitsubishi Q/L Serial", PlcProtocol.MitsubishiQlSerial)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new MitsubishiQlSerialClient(options);
        }
    }

    public sealed class OmronFinsProtocolDriver : LegacyProtocolDriver
    {
        public OmronFinsProtocolDriver()
            : base("legacy.omron-fins", "Omron FINS", PlcProtocol.OmronFins)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new FinsClient(options);
        }
    }

    public sealed class ModbusRtuProtocolDriver : LegacyProtocolDriver
    {
        public ModbusRtuProtocolDriver()
            : base("legacy.modbus-rtu", "Modbus RTU", PlcProtocol.ModbusRtu)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new ModbusRtuClient(options);
        }
    }

    public sealed class BacnetIpProtocolDriver : LegacyProtocolDriver
    {
        public BacnetIpProtocolDriver()
            : base("legacy.bacnet-ip", "BACnet/IP", PlcProtocol.BacnetIp)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new BacnetIpClient(options);
        }
    }

    public sealed class CanOpenProtocolDriver : LegacyProtocolDriver
    {
        public CanOpenProtocolDriver()
            : base("legacy.canopen", "CANopen", PlcProtocol.CanOpen)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new CanOpenClient(options);
        }
    }

    public sealed class OpcUaProtocolDriver : LegacyProtocolDriver
    {
        public OpcUaProtocolDriver()
            : base("legacy.opc-ua", "OPC UA", PlcProtocol.OpcUa)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new OpcUaClient(options);
        }
    }

    public sealed class OpcDaProtocolDriver : LegacyProtocolDriver
    {
        public OpcDaProtocolDriver()
            : base("legacy.opc-da", "OPC DA", PlcProtocol.OpcDa)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new OpcDaClient(options);
        }
    }

    public abstract class LegacyProtocolDriver : IProtocolDriver, IProtocolDriverMetadata, IPlcClientCapabilityProvider, IPlcTagDefinitionValidator
    {
        protected LegacyProtocolDriver(string driverId, string displayName, PlcProtocol protocol)
        {
            DriverId = driverId;
            DisplayName = displayName;
            Protocol = protocol;
        }

        public string DriverId { get; private set; }

        public string DisplayName { get; private set; }

        public PlcProtocol Protocol { get; private set; }

        public virtual bool Supports(PlcConnectionOptions options)
        {
            if (options == null)
                return false;

            if (string.Equals(options.DriverId, DriverId, StringComparison.OrdinalIgnoreCase))
                return true;

            return options.Protocol == Protocol && string.IsNullOrWhiteSpace(options.DriverId);
        }

        public abstract IPlcClient CreateClient(PlcConnectionOptions options);

        public virtual IList<PlcConnectionParameterDefinition> GetConnectionParameters()
        {
            return PlcConnectionParameterCatalog.ForProtocol(Protocol);
        }

        public virtual PlcClientCapabilities GetCapabilities()
        {
            return PlcClientCapabilityCatalog.ForProtocol(Protocol);
        }

        public virtual PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            return PlcProtocolTagValidator.Validate(Protocol, address, dataType, elementCount, elementOffset);
        }
    }
}
