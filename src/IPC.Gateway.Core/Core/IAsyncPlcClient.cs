using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public interface IAsyncPlcClient
    {
        ValueTask ConnectAsync(CancellationToken cancellationToken);
        ValueTask DisconnectAsync(CancellationToken cancellationToken);
        ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken);
        ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken);
    }
}
