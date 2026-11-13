namespace IPC.Plc.Communication.Core;

/// <summary>Releases remote resources for symbols removed from a live device configuration.</summary>
public interface IPlcAddressCache
{
    ValueTask RetainConfiguredAddressesAsync(IEnumerable<PlcBatchReadRequest> configured, CancellationToken cancellationToken);
}
