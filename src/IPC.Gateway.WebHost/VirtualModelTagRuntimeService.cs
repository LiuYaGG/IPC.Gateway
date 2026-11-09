using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Inference;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 在独立有界队列中执行虚拟模型标签，避免 ONNX 推理阻塞设备采集线程。
/// </summary>
public sealed class VirtualModelTagRuntimeService : BackgroundService
{
    private readonly GatewayCoreService _gateway;
    private readonly OnnxModelCatalogService _catalog;
    private readonly Channel<string> _queue;
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _definitionLock = new();
    private Dictionary<string, VirtualTagDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTime> _lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    /// <summary>
    /// 创建虚拟模型标签后台服务。
    /// </summary>
    public VirtualModelTagRuntimeService(GatewayCoreService gateway, OnnxModelCatalogService catalog)
    {
        _gateway = gateway;
        _catalog = catalog;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 订阅标签变化并启动刷新、周期调度和单线程推理消费循环。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _gateway.Runtime.TagValueChanged += OnTagValueChanged;
        try
        {
            RefreshDefinitions();
            Task scheduler = RunSchedulerAsync(stoppingToken);
            await ConsumeAsync(stoppingToken).ConfigureAwait(false);
            await scheduler.ConfigureAwait(false);
        }
        finally
        {
            _gateway.Runtime.TagValueChanged -= OnTagValueChanged;
        }
    }

    /// <summary>
    /// 每秒刷新虚拟标签定义并调度周期型或首次计算任务。
    /// </summary>
    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((DateTime.UtcNow - _lastRefreshUtc).TotalSeconds >= 2)
                RefreshDefinitions();

            List<VirtualTagDefinition> definitions;
            Dictionary<string, DateTime> lastRuns;
            lock (_definitionLock)
            {
                definitions = _definitions.Values.ToList();
                lastRuns = new Dictionary<string, DateTime>(_lastRuns, StringComparer.OrdinalIgnoreCase);
            }

            DateTime now = DateTime.UtcNow;
            foreach (VirtualTagDefinition definition in definitions)
            {
                bool interval = string.Equals(definition.Config.TriggerMode, "Interval", StringComparison.OrdinalIgnoreCase);
                bool neverRun = !lastRuns.TryGetValue(definition.Tag.Id, out DateTime last);
                if (neverRun || (interval && (now - last).TotalMilliseconds >= definition.Config.IntervalMilliseconds))
                    Enqueue(definition.Tag.Id);
            }
        }
    }

    /// <summary>
    /// 消费待计算标签并把任何异常转换为坏质量快照。
    /// </summary>
    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (string tagId in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _queued.TryRemove(tagId, out _);
            VirtualTagDefinition? definition;
            lock (_definitionLock)
                _definitions.TryGetValue(tagId, out definition);
            if (definition == null || !definition.Tag.Enabled || !_gateway.IsRunning)
                continue;
            try
            {
                Evaluate(definition);
            }
            catch (Exception ex)
            {
                PublishFailure(definition, ex.GetBaseException().Message);
            }
            lock (_definitionLock)
                _lastRuns[tagId] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 收集输入标签、执行固定发布版本并发布虚拟标签快照。
    /// </summary>
    private void Evaluate(VirtualTagDefinition definition)
    {
        VirtualModelTagConfig config = definition.Config;
        _ = _catalog.ResolvePublishedVersion(config.ModelId, config.ModelVersion);
        List<float> features = [];
        DateTime now = DateTime.Now;
        foreach (VirtualModelInputBindingConfig binding in config.Inputs)
        {
            string[] path = SplitPath(binding.TagPath);
            if (!_gateway.Runtime.TryGetSnapshotById(path[0], path[1], path[2], path[3], out TagValueSnapshot? input) || input == null)
                throw new InvalidOperationException($"未找到模型输入标签 {binding.TagPath}。");
            if (input.Quality != TagQuality.Good)
                throw new InvalidOperationException($"模型输入标签 {binding.TagPath} 的质量为 {input.Quality}。");
            if (input.Timestamp == DateTime.MinValue || (now - input.Timestamp).TotalMilliseconds > config.MaxInputAgeMilliseconds)
                throw new InvalidOperationException($"模型输入标签 {binding.TagPath} 已超过最大允许延迟。");
            if (!double.TryParse(input.ValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out double value) &&
                !double.TryParse(input.ValueText, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
                throw new InvalidOperationException($"模型输入标签 {binding.TagPath} 不是数值。");
            features.Add((float)(value * binding.Multiplier + binding.Offset));
        }

        ModelInferenceResult result = _catalog.Test(config.ModelId, new OnnxModelTestRequest
        {
            Version = config.ModelVersion,
            InputName = config.InputName,
            InputNames = config.InputNames,
            OutputName = config.OutputName,
            OutputIndex = config.OutputIndex,
            Features = features,
            TimeoutMilliseconds = config.TimeoutMilliseconds
        });
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage);

        object output = ConvertOutput(result.Score, definition.Tag.DataType, config.BoolThreshold);
        TagValueSnapshot snapshot = CreateBaseSnapshot(definition);
        snapshot.RawValue = result.Score;
        snapshot.RawValueText = result.Score.ToString("R", CultureInfo.InvariantCulture);
        snapshot.Value = output;
        snapshot.ValueText = FormatOutput(output);
        snapshot.DataType = definition.Tag.DataType.ToString();
        snapshot.Quality = TagQuality.Good;
        snapshot.TagState = "Good";
        snapshot.Timestamp = DateTime.Now;
        snapshot.CleaningApplied = true;
        snapshot.CleaningAction = "OnnxModel";
        snapshot.CleaningMessage = $"模型 {config.ModelId} v{config.ModelVersion} 推理成功，耗时 {result.DurationMilliseconds} ms。";
        _gateway.Runtime.PublishVirtualSnapshot(snapshot);
    }

    /// <summary>
    /// 根据失败策略保留上次值、使用回退值或发布空坏质量快照。
    /// </summary>
    private void PublishFailure(VirtualTagDefinition definition, string message)
    {
        TagValueSnapshot snapshot = CreateBaseSnapshot(definition);
        VirtualModelTagConfig config = definition.Config;
        if (string.Equals(config.FailurePolicy, "UseFallback", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.FallbackValue))
        {
            object fallback = ParseFallback(config.FallbackValue, definition.Tag.DataType, config.BoolThreshold);
            snapshot.Value = fallback;
            snapshot.ValueText = FormatOutput(fallback);
        }
        else if (string.Equals(config.FailurePolicy, "KeepLastGood", StringComparison.OrdinalIgnoreCase) &&
                 _gateway.Runtime.TryGetSnapshotById(definition.ChannelId, definition.Device.Id, definition.Group?.Id ?? string.Empty, definition.Tag.Id, out TagValueSnapshot? previous) &&
                 previous != null)
        {
            snapshot.RawValue = previous.RawValue;
            snapshot.RawValueText = previous.RawValueText;
            snapshot.Value = previous.Value;
            snapshot.ValueText = previous.ValueText;
        }
        snapshot.DataType = definition.Tag.DataType.ToString();
        snapshot.Quality = TagQuality.Bad;
        snapshot.TagState = "Bad";
        snapshot.Timestamp = DateTime.Now;
        snapshot.ErrorMessage = string.IsNullOrWhiteSpace(message) ? "虚拟模型标签执行失败。" : message;
        snapshot.CleaningApplied = true;
        snapshot.CleaningAction = "OnnxModelFailed";
        snapshot.CleaningMessage = snapshot.ErrorMessage;
        _gateway.Runtime.PublishVirtualSnapshot(snapshot);
    }

    /// <summary>
    /// 创建带完整标签身份和业务元数据的基础快照。
    /// </summary>
    private static TagValueSnapshot CreateBaseSnapshot(VirtualTagDefinition definition)
    {
        TagConfig tag = definition.Tag;
        return new TagValueSnapshot
        {
            ChannelId = definition.ChannelId,
            ChannelName = definition.ChannelName,
            DeviceId = definition.Device.Id,
            DeviceName = definition.Device.Name,
            DeviceProtocol = definition.Device.Protocol.ToString(),
            GroupId = definition.Group?.Id ?? string.Empty,
            GroupName = definition.Group?.Name ?? string.Empty,
            TagId = tag.Id,
            TagName = tag.Name,
            Unit = tag.Unit,
            PointCode = tag.PointCode,
            AssetPath = tag.AssetPath,
            BusinessType = tag.BusinessType,
            Source = "ONNX",
            Precision = tag.Precision,
            MqttPublishEnabled = tag.MqttPublishEnabled,
            Alarm = tag.Alarm ?? TagAlarmConfig.Default()
        };
    }

    /// <summary>
    /// 根据配置标签类型转换模型标量输出。
    /// </summary>
    private static object ConvertOutput(double value, PlcDataType type, double boolThreshold)
    {
        return type switch
        {
            PlcDataType.Bool or PlcDataType.Coil or PlcDataType.DiscreteInput => value >= boolThreshold,
            PlcDataType.Int8 => Convert.ToSByte(value),
            PlcDataType.UInt8 => Convert.ToByte(value),
            PlcDataType.Int16 => Convert.ToInt16(value),
            PlcDataType.UInt16 => Convert.ToUInt16(value),
            PlcDataType.Int32 => Convert.ToInt32(value),
            PlcDataType.UInt32 => Convert.ToUInt32(value),
            PlcDataType.Int64 => Convert.ToInt64(value),
            PlcDataType.UInt64 => Convert.ToUInt64(value),
            PlcDataType.Float => Convert.ToSingle(value),
            _ => value
        };
    }

    /// <summary>
    /// 将配置的回退文本转换成虚拟标签类型。
    /// </summary>
    private static object ParseFallback(string value, PlcDataType type, double threshold)
    {
        if (type is PlcDataType.Bool or PlcDataType.Coil or PlcDataType.DiscreteInput && bool.TryParse(value, out bool boolean))
            return boolean;
        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
            throw new InvalidOperationException("虚拟标签回退值无法转换为目标类型。");
        return ConvertOutput(number, type, threshold);
    }

    /// <summary>
    /// 使用不受区域设置影响的文本表示输出。
    /// </summary>
    private static string FormatOutput(object value) =>
        value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;

    /// <summary>
    /// 将一个虚拟标签加入有界队列并合并重复任务。
    /// </summary>
    private void Enqueue(string tagId)
    {
        if (_queued.TryAdd(tagId, 0) && !_queue.Writer.TryWrite(tagId))
            _queued.TryRemove(tagId, out _);
    }

    /// <summary>
    /// 标签变化后调度依赖它的事件型虚拟标签。
    /// </summary>
    private void OnTagValueChanged(object? sender, TagValueChangedEventArgs args)
    {
        string path = BuildPath(args.Snapshot.ChannelId, args.Snapshot.DeviceId, args.Snapshot.GroupId, args.Snapshot.TagId);
        List<string>? dependents;
        lock (_definitionLock)
            _dependencies.TryGetValue(path, out dependents);
        if (dependents == null)
            return;
        foreach (string tagId in dependents)
        {
            VirtualTagDefinition? definition;
            lock (_definitionLock)
                _definitions.TryGetValue(tagId, out definition);
            if (definition != null && !string.Equals(definition.Config.TriggerMode, "Interval", StringComparison.OrdinalIgnoreCase))
                ScheduleDebounced(definition);
        }
    }

    /// <summary>
    /// 在独立任务中执行轻量防抖，队列仍负责串行推理。
    /// </summary>
    private void ScheduleDebounced(VirtualTagDefinition definition)
    {
        int delay = Math.Clamp(definition.Config.DebounceMilliseconds, 0, 10000);
        _ = Task.Run(async () =>
        {
            if (delay > 0)
                await Task.Delay(delay).ConfigureAwait(false);
            Enqueue(definition.Tag.Id);
        });
    }

    /// <summary>
    /// 从当前项目重新构建虚拟标签定义与反向依赖索引。
    /// </summary>
    private void RefreshDefinitions()
    {
        Dictionary<string, VirtualTagDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> dependencies = new(StringComparer.OrdinalIgnoreCase);
        ProjectConfig project = _gateway.CurrentProject;
        foreach (DeviceConfig device in project.Devices ?? [])
        {
            string channelId = device.ChannelId ?? string.Empty;
            string channelName = project.Channels?.FirstOrDefault(item => string.Equals(item.Id, channelId, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
            AddTags(device.Tags, null);
            foreach (GroupConfig group in device.Groups ?? [])
                AddTags(group.Tags, group);

            void AddTags(IEnumerable<TagConfig>? tags, GroupConfig? group)
            {
                foreach (TagConfig tag in tags ?? [])
                {
                    if (!tag.IsVirtual || tag.VirtualModel == null)
                        continue;
                    VirtualTagDefinition definition = new(channelId, channelName, device, group, tag, tag.VirtualModel);
                    definitions[tag.Id] = definition;
                    foreach (VirtualModelInputBindingConfig input in tag.VirtualModel.Inputs ?? [])
                    {
                        string[] parts;
                        try { parts = SplitPath(input.TagPath); }
                        catch { continue; }
                        string path = BuildPath(parts[0], parts[1], parts[2], parts[3]);
                        if (!dependencies.TryGetValue(path, out List<string>? list))
                            dependencies[path] = list = [];
                        if (!list.Contains(tag.Id, StringComparer.OrdinalIgnoreCase))
                            list.Add(tag.Id);
                    }
                }
            }
        }
        lock (_definitionLock)
        {
            _definitions = definitions;
            _dependencies = dependencies;
            foreach (string removed in _lastRuns.Keys.Where(id => !definitions.ContainsKey(id)).ToList())
                _lastRuns.Remove(removed);
            _lastRefreshUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 解析四段式标签路径。
    /// </summary>
    private static string[] SplitPath(string path)
    {
        string[] parts = (path ?? string.Empty).Split('/');
        if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[3]))
            throw new InvalidOperationException($"标签路径 {path} 不是 ChannelId/DeviceId/GroupId/TagId 格式。");
        return parts.Select(item => item.Trim()).ToArray();
    }

    /// <summary>
    /// 构造不区分大小写的标签依赖键。
    /// </summary>
    private static string BuildPath(string channelId, string deviceId, string groupId, string tagId) =>
        string.Join("/", channelId, deviceId, groupId, tagId).ToLowerInvariant();

    /// <summary>
    /// 保存运行时计算所需的标签及父级元数据。
    /// </summary>
    private sealed record VirtualTagDefinition(
        string ChannelId,
        string ChannelName,
        DeviceConfig Device,
        GroupConfig? Group,
        TagConfig Tag,
        VirtualModelTagConfig Config);
}
