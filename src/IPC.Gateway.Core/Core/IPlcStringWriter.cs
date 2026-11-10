using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core;

/// <summary>Writes a string using the capacity configured on the tag, excluding protocol headers.</summary>
public interface IPlcStringWriter
{
    ValueTask WriteStringAsync(string address, string value, int capacity, int elementOffset, CancellationToken cancellationToken);
}
