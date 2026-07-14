/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Infrastructure
* 项目描述 ：
* 类 名 称 ：ProtocolDriverBase
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Infrastructure
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
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Metering.Cjt188;
using IPC.Plc.Communication.Metering.Dlt645;
using IPC.Plc.Communication.ModbusTcp;
using IPC.Plc.Communication.VirtualPlc;

namespace IPC.Plc.Communication.Infrastructure
{
    public abstract class ProtocolDriverBase : IProtocolDriver, IProtocolDriverMetadata, IPlcClientCapabilityProvider, IPlcTagDefinitionValidator
    {
        protected ProtocolDriverBase(string driverId, string displayName, PlcProtocol protocol)
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
            if (!string.IsNullOrWhiteSpace(options.DriverId))
                return string.Equals(options.DriverId.Trim(), DriverId, StringComparison.OrdinalIgnoreCase);
            return options.Protocol == Protocol;
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

    public sealed class ModbusTcpProtocolDriver : ProtocolDriverBase
    {
        public ModbusTcpProtocolDriver()
            : base("builtin.modbus-tcp", "Built-in Modbus TCP", PlcProtocol.ModbusTcp)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new ModbusTcpClient(options);
        }
    }

    public sealed class Dlt645ProtocolDriver : ProtocolDriverBase
    {
        public Dlt645ProtocolDriver()
            : base("builtin.dlt645-2007", "Built-in DL/T 645-2007", PlcProtocol.Dlt6452007)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new Dlt645Client(options);
        }
    }

    public sealed class Cjt188ProtocolDriver : ProtocolDriverBase
    {
        public Cjt188ProtocolDriver()
            : base("builtin.cjt188-2004", "Built-in CJ/T 188-2004", PlcProtocol.Cjt1882004)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new Cjt188Client(options);
        }
    }

    public sealed class VirtualPlcProtocolDriver : ProtocolDriverBase
    {
        public VirtualPlcProtocolDriver()
            : base("builtin.virtual-plc", "Built-in Virtual PLC", PlcProtocol.VirtualPlc)
        {
        }

        public override IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new VirtualPlcClient(options);
        }
    }
}
