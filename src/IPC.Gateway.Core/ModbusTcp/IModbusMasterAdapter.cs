namespace IPC.Plc.Communication.ModbusTcp;

internal interface IModbusMasterAdapter : IDisposable
{
    bool[] ReadBits(bool discreteInputs, int startAddress, int count);
    byte[] ReadRegisters(bool inputRegisters, int startAddress, int count);
    void WriteBits(int startAddress, bool[] values);
    void WriteRegisters(int startAddress, byte[] data);
    ValueTask<bool[]> ReadBitsAsync(bool discreteInputs, int startAddress, int count, CancellationToken cancellationToken);
    ValueTask<byte[]> ReadRegistersAsync(bool inputRegisters, int startAddress, int count, CancellationToken cancellationToken);
    ValueTask WriteBitsAsync(int startAddress, bool[] values, CancellationToken cancellationToken);
    ValueTask WriteRegistersAsync(int startAddress, byte[] data, CancellationToken cancellationToken);
}
