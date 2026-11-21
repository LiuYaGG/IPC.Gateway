using System.Buffers.Binary;
using NModbus.IO;

namespace IPC.Plc.Communication.ModbusTcp;

// NModbus reads the MBAP prefix and payload separately. Reassemble only bytes actually
// received from the socket. Ordinary acquisition continues to use its original adapter.
internal sealed class ModbusDiagnosticStream(IStreamResource inner, Action<string, ReadOnlyMemory<byte>> capture) : IStreamResource
{
    private readonly byte[] _response = new byte[260];
    private int _received;
    private int _expected = 6;

    public int InfiniteTimeout => inner.InfiniteTimeout;
    public int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }
    public int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }
    public void DiscardInBuffer() { FlushReceived(); inner.DiscardInBuffer(); }
    public void Dispose() { FlushReceived(); inner.Dispose(); }

    public void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        capture("TX", buffer.AsMemory(offset, count));
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        var bytes = buffer.AsSpan(offset, read);
        while (!bytes.IsEmpty)
        {
            int length = Math.Min(bytes.Length, _expected - _received);
            bytes[..length].CopyTo(_response.AsSpan(_received));
            _received += length;
            bytes = bytes[length..];
            if (_received != _expected) continue;
            if (_received == 6)
            {
                int payload = BinaryPrimitives.ReadUInt16BigEndian(_response.AsSpan(4, 2));
                if (payload is > 0 and <= 254) { _expected += payload; continue; }
            }
            FlushReceived();
        }
        return read;
    }

    private void FlushReceived()
    {
        if (_received > 0) capture("RX", _response.AsMemory(0, _received));
        _received = 0;
        _expected = 6;
    }
}
