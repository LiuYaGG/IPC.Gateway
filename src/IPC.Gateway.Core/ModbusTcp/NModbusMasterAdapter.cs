using NModbus;

namespace IPC.Plc.Communication.ModbusTcp;

/// <summary>
/// Keeps the gateway's Modbus limits and byte representation independent from
/// the NModbus transport used by TCP, RTU and ASCII drivers.
/// </summary>
public sealed class NModbusMasterAdapter : IDisposable
{
    public const int MaxReadBits = 2000;
    public const int MaxWriteBits = 1968;
    public const int MaxReadRegisters = 125;
    public const int MaxWriteRegisters = 123;

    private readonly IModbusMaster _master;
    private readonly byte _unitId;
    private readonly SemaphoreSlim _operationLock;
    private readonly bool _ownsMaster;
    private readonly bool _ownsOperationLock;
    private bool _disposed;

    public NModbusMasterAdapter(IModbusMaster master, byte unitId, int timeoutMilliseconds)
        : this(master, unitId, timeoutMilliseconds, new SemaphoreSlim(1, 1), true, true)
    {
    }

    private NModbusMasterAdapter(
        IModbusMaster master,
        byte unitId,
        int timeoutMilliseconds,
        SemaphoreSlim operationLock,
        bool ownsMaster,
        bool ownsOperationLock)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _unitId = unitId;
        _operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
        _ownsMaster = ownsMaster;
        _ownsOperationLock = ownsOperationLock;

        int timeout = timeoutMilliseconds > 0 ? timeoutMilliseconds : 3000;
        _master.Transport.ReadTimeout = timeout;
        _master.Transport.WriteTimeout = timeout;
        _master.Transport.Retries = 0;
    }

    public static NModbusMasterAdapter CreateShared(
        IModbusMaster master,
        byte unitId,
        int timeoutMilliseconds,
        SemaphoreSlim operationLock)
    {
        return new NModbusMasterAdapter(
            master,
            unitId,
            timeoutMilliseconds,
            operationLock,
            false,
            false);
    }

    public bool[] ReadBits(bool discreteInputs, int startAddress, int count)
    {
        ThrowIfDisposed();
        _operationLock.Wait();
        try
        {
            bool[] result = new bool[count];
            int copied = 0;
            while (copied < count)
            {
                ushort segmentCount = ToUInt16(Math.Min(MaxReadBits, count - copied));
                ushort address = ToUInt16(startAddress + copied);
                bool[] segment = discreteInputs
                    ? _master.ReadInputs(_unitId, address, segmentCount)
                    : _master.ReadCoils(_unitId, address, segmentCount);
                Array.Copy(segment, 0, result, copied, segmentCount);
                copied += segmentCount;
            }
            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public byte[] ReadRegisters(bool inputRegisters, int startAddress, int count)
    {
        ThrowIfDisposed();
        _operationLock.Wait();
        try
        {
            ushort[] result = new ushort[count];
            int copied = 0;
            while (copied < count)
            {
                ushort segmentCount = ToUInt16(Math.Min(MaxReadRegisters, count - copied));
                ushort address = ToUInt16(startAddress + copied);
                ushort[] segment = inputRegisters
                    ? _master.ReadInputRegisters(_unitId, address, segmentCount)
                    : _master.ReadHoldingRegisters(_unitId, address, segmentCount);
                Array.Copy(segment, 0, result, copied, segmentCount);
                copied += segmentCount;
            }
            return ToBigEndianBytes(result);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void WriteBits(int startAddress, bool[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ThrowIfDisposed();
        _operationLock.Wait();
        try
        {
            int written = 0;
            while (written < values.Length)
            {
                int segmentCount = Math.Min(MaxWriteBits, values.Length - written);
                ushort address = ToUInt16(startAddress + written);
                if (segmentCount == 1)
                {
                    _master.WriteSingleCoil(_unitId, address, values[written]);
                }
                else
                {
                    bool[] segment = new bool[segmentCount];
                    Array.Copy(values, written, segment, 0, segmentCount);
                    _master.WriteMultipleCoils(_unitId, address, segment);
                }
                written += segmentCount;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void WriteRegisters(int startAddress, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ushort[] values = FromBigEndianBytes(data);
        ThrowIfDisposed();
        _operationLock.Wait();
        try
        {
            WriteRegisterSegments(startAddress, values);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask<bool[]> ReadBitsAsync(
        bool discreteInputs,
        int startAddress,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool[] result = new bool[count];
            int copied = 0;
            while (copied < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort segmentCount = ToUInt16(Math.Min(MaxReadBits, count - copied));
                ushort address = ToUInt16(startAddress + copied);
                bool[] segment = discreteInputs
                    ? await _master.ReadInputsAsync(_unitId, address, segmentCount).ConfigureAwait(false)
                    : await _master.ReadCoilsAsync(_unitId, address, segmentCount).ConfigureAwait(false);
                Array.Copy(segment, 0, result, copied, segmentCount);
                copied += segmentCount;
            }
            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask<byte[]> ReadRegistersAsync(
        bool inputRegisters,
        int startAddress,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ushort[] result = new ushort[count];
            int copied = 0;
            while (copied < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort segmentCount = ToUInt16(Math.Min(MaxReadRegisters, count - copied));
                ushort address = ToUInt16(startAddress + copied);
                ushort[] segment = inputRegisters
                    ? await _master.ReadInputRegistersAsync(_unitId, address, segmentCount).ConfigureAwait(false)
                    : await _master.ReadHoldingRegistersAsync(_unitId, address, segmentCount).ConfigureAwait(false);
                Array.Copy(segment, 0, result, copied, segmentCount);
                copied += segmentCount;
            }
            return ToBigEndianBytes(result);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask WriteBitsAsync(
        int startAddress,
        bool[] values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int written = 0;
            while (written < values.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentCount = Math.Min(MaxWriteBits, values.Length - written);
                ushort address = ToUInt16(startAddress + written);
                if (segmentCount == 1)
                {
                    await _master.WriteSingleCoilAsync(_unitId, address, values[written]).ConfigureAwait(false);
                }
                else
                {
                    bool[] segment = new bool[segmentCount];
                    Array.Copy(values, written, segment, 0, segmentCount);
                    await _master.WriteMultipleCoilsAsync(_unitId, address, segment).ConfigureAwait(false);
                }
                written += segmentCount;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask WriteRegistersAsync(
        int startAddress,
        byte[] data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ushort[] values = FromBigEndianBytes(data);
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int written = 0;
            while (written < values.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentCount = Math.Min(MaxWriteRegisters, values.Length - written);
                ushort address = ToUInt16(startAddress + written);
                if (segmentCount == 1)
                {
                    await _master.WriteSingleRegisterAsync(_unitId, address, values[written]).ConfigureAwait(false);
                }
                else
                {
                    ushort[] segment = new ushort[segmentCount];
                    Array.Copy(values, written, segment, 0, segmentCount);
                    await _master.WriteMultipleRegistersAsync(_unitId, address, segment).ConfigureAwait(false);
                }
                written += segmentCount;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsMaster)
            _master.Dispose();
        if (_ownsOperationLock)
            _operationLock.Dispose();
    }

    private void WriteRegisterSegments(int startAddress, ushort[] values)
    {
        int written = 0;
        while (written < values.Length)
        {
            int segmentCount = Math.Min(MaxWriteRegisters, values.Length - written);
            ushort address = ToUInt16(startAddress + written);
            if (segmentCount == 1)
            {
                _master.WriteSingleRegister(_unitId, address, values[written]);
            }
            else
            {
                ushort[] segment = new ushort[segmentCount];
                Array.Copy(values, written, segment, 0, segmentCount);
                _master.WriteMultipleRegisters(_unitId, address, segment);
            }
            written += segmentCount;
        }
    }

    private static ushort[] FromBigEndianBytes(byte[] data)
    {
        int byteCount = data.Length % 2 == 0 ? data.Length : data.Length + 1;
        ushort[] registers = new ushort[byteCount / 2];
        for (int i = 0; i < registers.Length; i++)
        {
            int offset = i * 2;
            byte low = offset + 1 < data.Length ? data[offset + 1] : (byte)0;
            registers[i] = (ushort)((data[offset] << 8) | low);
        }
        return registers;
    }

    private static byte[] ToBigEndianBytes(ushort[] registers)
    {
        byte[] data = new byte[registers.Length * 2];
        for (int i = 0; i < registers.Length; i++)
        {
            data[i * 2] = (byte)(registers[i] >> 8);
            data[i * 2 + 1] = (byte)registers[i];
        }
        return data;
    }

    private static ushort ToUInt16(int value)
    {
        if (value < 0 || value > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Modbus address or count is outside the UInt16 range.");
        return (ushort)value;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
