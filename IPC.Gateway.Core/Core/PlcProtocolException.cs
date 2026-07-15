using System;
using System.IO;
using System.Net.Sockets;
using NModbus;

namespace IPC.Plc.Communication.Core
{
    /// <summary>
    /// Carries the protocol error code and the smallest runtime scope affected by it.
    /// Drivers should prefer this exception to message based error classification.
    /// </summary>
    public sealed class PlcProtocolException : Exception
    {
        public PlcProtocolException(
            PlcReadFailureScope failureScope,
            string message,
            string? errorCode = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            FailureScope = failureScope == PlcReadFailureScope.None
                ? PlcReadFailureScope.Tag
                : failureScope;
            ErrorCode = errorCode ?? string.Empty;
        }

        public PlcReadFailureScope FailureScope { get; }

        public string ErrorCode { get; }
    }

    public static class PlcFailureClassifier
    {
        public static PlcReadFailureScope Classify(Exception? exception, PlcReadFailureScope fallback)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is PlcProtocolException protocolException)
                    return protocolException.FailureScope;

                if (current is SlaveException slaveException)
                {
                    int code = Convert.ToInt32(slaveException.SlaveExceptionCode);
                    return code == 1 || code == 2 || code == 3
                        ? PlcReadFailureScope.Tag
                        : code == 10 || code == 11
                            ? PlcReadFailureScope.Transport
                            : PlcReadFailureScope.Device;
                }

                if (current is PlcTagException)
                    return PlcReadFailureScope.Tag;

                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return PlcReadFailureScope.Transport;
            }

            return fallback == PlcReadFailureScope.None ? PlcReadFailureScope.Tag : fallback;
        }

        public static bool IsConnectionFailure(Exception? exception)
        {
            return PlcBatchReadResult.IsConnectionFailureScope(
                Classify(exception, PlcReadFailureScope.Tag));
        }
    }
}
