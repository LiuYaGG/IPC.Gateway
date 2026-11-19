namespace BeckhoffAdsTool;

public partial class MainForm : Form
{
    private readonly AdsClientService _service = new();
    private bool _busy;

    public MainForm()
    {
        InitializeComponent();
        dataTypeComboBox.DataSource = Enum.GetValues<AdsValueType>();
        dataTypeComboBox.SelectedItem = AdsValueType.Int16;
        connectButton.Click += async (_, _) => await ConnectOrDisconnectAsync();
        readButton.Click += async (_, _) => await RunAsync(ReadCoreAsync);
        writeButton.Click += async (_, _) => await RunAsync(WriteCoreAsync);
        writeReadButton.Click += async (_, _) => await RunAsync(async () =>
        {
            await WriteCoreAsync();
            await ReadCoreAsync();
        });
        clearLogButton.Click += (_, _) => logTextBox.Clear();
        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            _service.Dispose();
        };
        UpdateConnectionState();
        Log("请输入 ADS 连接参数，然后点击“连接”。");
    }

    private AdsValueType SelectedType => (AdsValueType)dataTypeComboBox.SelectedItem!;
    private int Count => decimal.ToInt32(countInput.Value);

    private async Task ConnectOrDisconnectAsync()
    {
        if (_service.IsConnected)
        {
            _service.Disconnect();
            Log("已断开 ADS 连接。");
            UpdateConnectionState();
            return;
        }

        await RunAsync(async () =>
        {
            await _service.ConnectAsync(
                amsNetIdTextBox.Text,
                decimal.ToInt32(adsPortInput.Value),
                decimal.ToInt32(timeoutInput.Value),
                CancellationToken.None);
            Log($"ADS 连接成功，设备状态正常：{amsNetIdTextBox.Text.Trim()}:{adsPortInput.Value}");
        });
        UpdateConnectionState();
    }

    private async Task ReadCoreAsync()
    {
        string symbol = symbolTextBox.Text.Trim();
        AdsValueType type = SelectedType;
        int byteCount = AdsValueCodec.GetReadSize(type, Count);
        byte[] data = await _service.ReadAsync(symbol, byteCount, CancellationToken.None);
        string value = AdsValueCodec.Decode(type, data, Count);
        valueTextBox.Text = value;
        Log($"读取成功：{symbol} [{type}] = {value}");
    }

    private async Task WriteCoreAsync()
    {
        string symbol = symbolTextBox.Text.Trim();
        AdsValueType type = SelectedType;
        byte[] data = AdsValueCodec.Encode(type, valueTextBox.Text, Count);
        await _service.WriteAsync(symbol, data, CancellationToken.None);
        Log($"写入成功：{symbol} [{type}]，{data.Length} 字节。");
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
            return;
        _busy = true;
        UseWaitCursor = true;
        UpdateConnectionState();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log($"错误：{ex.Message}");
            MessageBox.Show(this, ex.Message, "ADS 操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            UpdateConnectionState();
        }
    }

    private void UpdateConnectionState()
    {
        bool connected = _service.IsConnected;
        connectionGroup.Enabled = !_busy;
        tagGroup.Enabled = !_busy;
        connectButton.Text = connected ? "断开" : "连接";
        readButton.Enabled = !_busy && connected;
        writeButton.Enabled = !_busy && connected;
        writeReadButton.Enabled = !_busy && connected;
        clearLogButton.Enabled = !_busy;
        statusLabel.Text = connected ? "● 已连接，ADS 设备状态查询正常" : "○ 未连接";
        statusLabel.ForeColor = connected ? Color.SeaGreen : Color.DimGray;
    }

    private void Log(string message)
    {
        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
