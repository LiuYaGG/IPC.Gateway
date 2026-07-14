using System;
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration
{
    /// <summary>
    /// Groups devices that use the same protocol driver and owns their polling policy.
    /// Physical endpoint information remains on each device.
    /// </summary>
    public sealed class ChannelConfig
    {
        public ChannelConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "通道";
            Enabled = true;
            Protocol = PlcProtocol.ModbusTcp;
            DriverId = string.Empty;
            MaxConcurrentDevicePolls = 4;
            SchedulingWeight = 1;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public PlcProtocol Protocol { get; set; }
        public string DriverId { get; set; }
        public int MaxConcurrentDevicePolls { get; set; }
        public int SchedulingWeight { get; set; }
    }
}
