using System;
using System.Threading;

namespace IPC.Plc.Communication.Core;

/// <summary>Opt-in, request-scoped observation of frames actually sent or received by a driver.</summary>
public static class PlcDiagnosticTrace
{
    private static readonly AsyncLocal<Action<string, ReadOnlyMemory<byte>>?> Listener = new();

    public static Action<string, ReadOnlyMemory<byte>>? Current => Listener.Value;

    public static void Record(string direction, byte[] frame) => Current?.Invoke(direction, frame);

    public static void Record(string direction, byte[] header, byte[] body)
    {
        var capture = Current;
        if (capture == null) return;
        if (body.Length == 0) { capture(direction, header); return; }
        byte[] frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, header.Length);
        capture(direction, frame);
    }

    public static IDisposable Begin(Action<string, ReadOnlyMemory<byte>> listener) => new Scope(listener);

    private sealed class Scope(Action<string, ReadOnlyMemory<byte>> listener) : IDisposable
    {
        private readonly Action<string, ReadOnlyMemory<byte>>? _previous = Install(listener);
        public void Dispose() => Listener.Value = _previous;

        private static Action<string, ReadOnlyMemory<byte>>? Install(Action<string, ReadOnlyMemory<byte>> next)
        {
            var previous = Listener.Value;
            Listener.Value = next;
            return previous;
        }
    }
}
