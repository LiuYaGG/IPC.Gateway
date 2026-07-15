using System;

namespace IPC.Plc.Communication.CanOpen
{
    internal enum CanOpenNodeState : byte
    {
        BootUp = 0,
        Stopped = 4,
        Operational = 5,
        PreOperational = 127,
        Unknown = 255
    }

    internal sealed record CanOpenHeartbeatState(
        int NodeId,
        CanOpenNodeState State,
        byte RawState,
        DateTime TimestampUtc);

    internal sealed record CanOpenEmergencyState(
        int NodeId,
        ushort ErrorCode,
        byte ErrorRegister,
        byte[] ManufacturerData,
        DateTime TimestampUtc);

    internal sealed record CanOpenPdoValue(
        int PdoNumber,
        int NodeId,
        byte[] Data,
        DateTime TimestampUtc);
}
