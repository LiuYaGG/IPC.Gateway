using TwinCAT.Ads;

namespace BeckhoffAdsTool;

/// <summary>
/// 对倍福官方 AdsClient 的轻量封装。每次操作使用独立符号句柄，并在结束后释放。
/// </summary>
public sealed class AdsClientService : IDisposable
{
    private AdsClient? _client;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(string amsNetId, int adsPort, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        Disconnect();
        var client = new AdsClient { Timeout = timeoutMilliseconds };
        try
        {
            await client.ConnectAsync(AmsNetId.Parse(amsNetId.Trim()), adsPort, cancellationToken);
            ResultReadDeviceState state = await client.ReadStateAsync(cancellationToken);
            if (!state.Succeeded)
                throw new InvalidOperationException($"ADS 设备状态读取失败：{state.ErrorCode}");
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(string symbol, int byteCount, CancellationToken cancellationToken)
    {
        AdsClient client = GetConnectedClient();
        ResultHandle handle = await CreateHandleAsync(client, symbol, cancellationToken);
        try
        {
            var result = await client.ReadAsync(handle.Handle, byteCount, cancellationToken);
            if (!result.Succeeded)
                throw new InvalidOperationException($"读取 {symbol} 失败：{result.ErrorCode}");
            return result.Data.ToArray();
        }
        finally
        {
            await client.DeleteVariableHandleAsync(handle.Handle, cancellationToken);
        }
    }

    public async Task WriteAsync(string symbol, byte[] data, CancellationToken cancellationToken)
    {
        AdsClient client = GetConnectedClient();
        ResultHandle handle = await CreateHandleAsync(client, symbol, cancellationToken);
        try
        {
            ResultWrite result = await client.WriteAsync(handle.Handle, data.AsMemory(), cancellationToken);
            if (!result.Succeeded)
                throw new InvalidOperationException($"写入 {symbol} 失败：{result.ErrorCode}");
        }
        finally
        {
            await client.DeleteVariableHandleAsync(handle.Handle, cancellationToken);
        }
    }

    public void Disconnect()
    {
        if (_client is null)
            return;
        try
        {
            _client.Disconnect();
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    public void Dispose() => Disconnect();

    private static async Task<ResultHandle> CreateHandleAsync(
        AdsClient client,
        string symbol,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("请输入 TwinCAT 符号地址，例如 GVL.iCounter。", nameof(symbol));
        ResultHandle handle = await client.CreateVariableHandleAsync(symbol.Trim(), cancellationToken);
        if (!handle.Succeeded)
            throw new InvalidOperationException($"解析符号 {symbol} 失败：{handle.ErrorCode}");
        return handle;
    }

    private AdsClient GetConnectedClient()
    {
        if (_client is null || !_client.IsConnected)
            throw new InvalidOperationException("尚未连接 ADS 设备。");
        return _client;
    }
}
