using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeConfigDiffer
    {
        public bool CanReuseDeviceState(DeviceConfig? previous, DeviceConfig? current)
        {
            return DeviceConfigComparer.CanReuseRuntimeState(previous, current) ||
                   DeviceConfigComparer.CanReuseRuntimeStateForEnabledChange(previous, current);
        }
    }
}
