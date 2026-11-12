using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Plc.Communication.ModbusUdp;

/// <summary>Datagram transport with per-device serialization, bounded waits and no write retries.</summary>
internal sealed class ModbusUdpMasterAdapter : IModbusMasterAdapter
{
    private readonly UdpClient _client;
    private readonly byte _unitId;
    private readonly int _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ushort _transactionId = (ushort)RandomNumberGenerator.GetInt32(1, 65536);
    private bool _disposed;

    public ModbusUdpMasterAdapter(UdpClient client, byte unitId, int timeout)
    {
        _client = client;
        _unitId = unitId;
        _timeout = timeout > 0 ? timeout : 3000;
    }

    public bool[] ReadBits(bool discreteInputs, int startAddress, int count) =>
        ReadBitsAsync(discreteInputs, startAddress, count, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public byte[] ReadRegisters(bool inputRegisters, int startAddress, int count) =>
        ReadRegistersAsync(inputRegisters, startAddress, count, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public void WriteBits(int startAddress, bool[] values) =>
        WriteBitsAsync(startAddress, values, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public void WriteRegisters(int startAddress, byte[] data) =>
        WriteRegistersAsync(startAddress, data, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public async ValueTask<bool[]> ReadBitsAsync(bool discreteInputs, int startAddress, int count, CancellationToken cancellationToken)
    {
        ValidateRange(startAddress, count);
        var result = new bool[count];
        for (int offset = 0; offset < count;)
        {
            int length = Math.Min(NModbusMasterAdapter.MaxReadBits, count - offset);
            byte[] response = await ExchangeAsync(AddressCount(discreteInputs ? (byte)2 : (byte)1, startAddress + offset, length), cancellationToken).ConfigureAwait(false);
            int byteCount = (length + 7) / 8;
            ValidateReadResponse(response, byteCount);
            for (int i = 0; i < length; i++) result[offset + i] = (response[2 + i / 8] & (1 << (i % 8))) != 0;
            offset += length;
        }
        return result;
    }

    public async ValueTask<byte[]> ReadRegistersAsync(bool inputRegisters, int startAddress, int count, CancellationToken cancellationToken)
    {
        ValidateRange(startAddress, count);
        var result = new byte[count * 2];
        for (int offset = 0; offset < count;)
        {
            int length = Math.Min(NModbusMasterAdapter.MaxReadRegisters, count - offset);
            byte[] response = await ExchangeAsync(AddressCount(inputRegisters ? (byte)4 : (byte)3, startAddress + offset, length), cancellationToken).ConfigureAwait(false);
            ValidateReadResponse(response, length * 2);
            response.AsSpan(2).CopyTo(result.AsSpan(offset * 2));
            offset += length;
        }
        return result;
    }

    public async ValueTask WriteBitsAsync(int startAddress, bool[] values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateRange(startAddress, values.Length);
        for (int offset = 0; offset < values.Length;)
        {
            int length = Math.Min(NModbusMasterAdapter.MaxWriteBits, values.Length - offset);
            byte[] request;
            if (length == 1) request = AddressCount(5, startAddress + offset, values[offset] ? 0xff00 : 0);
            else
            {
                int byteCount = (length + 7) / 8;
                request = new byte[6 + byteCount];
                AddressCount(15, startAddress + offset, length).CopyTo(request, 0);
                request[5] = (byte)byteCount;
                for (int i = 0; i < length; i++)
                    if (values[offset + i]) request[6 + i / 8] |= (byte)(1 << (i % 8));
            }
            ValidateWriteResponse(request, await ExchangeAsync(request, cancellationToken).ConfigureAwait(false));
            offset += length;
        }
    }

    public async ValueTask WriteRegistersAsync(int startAddress, byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((data.Length & 1) != 0) throw new ArgumentException("Modbus register writes require an even byte count.", nameof(data));
        ValidateRange(startAddress, data.Length / 2);
        for (int offset = 0; offset < data.Length / 2;)
        {
            int length = Math.Min(NModbusMasterAdapter.MaxWriteRegisters, data.Length / 2 - offset);
            byte[] request;
            if (length == 1) request = AddressCount(6, startAddress + offset, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset * 2)));
            else
            {
                request = new byte[6 + length * 2];
                AddressCount(16, startAddress + offset, length).CopyTo(request, 0);
                request[5] = (byte)(length * 2);
                data.AsSpan(offset * 2, length * 2).CopyTo(request.AsSpan(6));
            }
            ValidateWriteResponse(request, await ExchangeAsync(request, cancellationToken).ConfigureAwait(false));
            offset += length;
        }
    }

    private async ValueTask<byte[]> ExchangeAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(_timeout);
        bool entered = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            _transactionId = _transactionId == ushort.MaxValue ? (ushort)1 : (ushort)(_transactionId + 1);
            var request = new byte[7 + pdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(request, _transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)(pdu.Length + 1));
            request[6] = _unitId;
            pdu.CopyTo(request, 7);
            await _client.SendAsync(request, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                byte[] frame = (await _client.ReceiveAsync(deadline.Token).ConfigureAwait(false)).Buffer;
                // Connected UDP filters the peer endpoint. Ignore delayed/duplicate replies
                // from previous requests without extending the original deadline.
                if (frame.Length < 2 || BinaryPrimitives.ReadUInt16BigEndian(frame) != _transactionId) continue;
                if (frame.Length < 8 || frame.Length > 260 ||
                    BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2)) != 0 ||
                    BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4)) != frame.Length - 6)
                    throw new IOException("Invalid Modbus UDP MBAP header or datagram length.");
                if (frame[6] != _unitId) continue;
                byte function = frame[7];
                if (function == (pdu[0] | 0x80))
                {
                    if (frame.Length != 9) throw new IOException("Invalid Modbus UDP exception response length.");
                    byte code = frame[8];
                    var scope = code is 1 or 2 or 3 ? PlcReadFailureScope.Tag
                        : code is 10 or 11 ? PlcReadFailureScope.Transport : PlcReadFailureScope.Device;
                    throw new PlcProtocolException(scope, $"Modbus UDP exception: function 0x{pdu[0]:X2}, code 0x{code:X2}.", $"MODBUS-{code:X2}");
                }
                if (function != pdu[0]) throw new IOException("Modbus UDP response function does not match the request.");
                return frame.AsSpan(7).ToArray();
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"Modbus UDP response timed out after {_timeout} ms.", error);
        }
        finally { if (entered) _gate.Release(); }
    }

    private static void ValidateReadResponse(byte[] pdu, int byteCount)
    {
        if (pdu.Length != byteCount + 2 || pdu[1] != byteCount)
            throw new IOException("Modbus UDP read response byte count does not match the request.");
    }

    private static void ValidateWriteResponse(byte[] request, byte[] response)
    {
        if (response.Length != 5 || !request.AsSpan(0, 5).SequenceEqual(response))
            throw new IOException("Modbus UDP write response does not echo the requested address and value/count.");
    }

    private static byte[] AddressCount(byte function, int address, int count)
    {
        var pdu = new byte[5]; pdu[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), checked((ushort)address));
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), checked((ushort)count));
        return pdu;
    }

    private static void ValidateRange(int start, int count)
    {
        if (start < 0 || count <= 0 || (long)start + count > 65536)
            throw new ArgumentOutOfRangeException(nameof(count), "Modbus address range must be within 0..65535.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _client.Dispose();
        // Do not dispose the semaphore while an interrupted receive is releasing it.
    }
}
