/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.DataProcessing
* 项目描述 ：
* 类 名 称 ：EdgeDataProcessingOptions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.DataProcessing
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
namespace IPC.Gateway.DataProcessing;

public sealed class EdgeDataProcessingOptions
{
    public EdgeDataProcessingOptions()
    {
        AggregationMethods = "Average,Min,Max,Count";
        FillMode = "Previous";
        MaxSyntheticPointsPerInput = 1000;
    }

    public bool Enabled { get; set; }
    public bool CompressionEnabled { get; set; }
    public double CompressionTolerance { get; set; }
    public bool CompressDuplicateText { get; set; } = true;
    public bool DownsamplingEnabled { get; set; }
    public int DownsamplingIntervalMs { get; set; }
    public bool AlignmentEnabled { get; set; }
    public int AlignmentIntervalMs { get; set; }
    public bool FillEnabled { get; set; }
    public int FillIntervalMs { get; set; }
    public int FillMaxGapSeconds { get; set; }
    public string FillMode { get; set; }
    public bool AggregationEnabled { get; set; }
    public int AggregationIntervalSeconds { get; set; }
    public string AggregationMethods { get; set; }
    public int MaxSyntheticPointsPerInput { get; set; }

    public EdgeDataProcessingOptions Clone()
    {
        return new EdgeDataProcessingOptions
        {
            Enabled = Enabled,
            CompressionEnabled = CompressionEnabled,
            CompressionTolerance = CompressionTolerance,
            CompressDuplicateText = CompressDuplicateText,
            DownsamplingEnabled = DownsamplingEnabled,
            DownsamplingIntervalMs = DownsamplingIntervalMs,
            AlignmentEnabled = AlignmentEnabled,
            AlignmentIntervalMs = AlignmentIntervalMs,
            FillEnabled = FillEnabled,
            FillIntervalMs = FillIntervalMs,
            FillMaxGapSeconds = FillMaxGapSeconds,
            FillMode = FillMode,
            AggregationEnabled = AggregationEnabled,
            AggregationIntervalSeconds = AggregationIntervalSeconds,
            AggregationMethods = AggregationMethods,
            MaxSyntheticPointsPerInput = MaxSyntheticPointsPerInput
        };
    }

    public static EdgeDataProcessingOptions Normalize(EdgeDataProcessingOptions? options)
    {
        EdgeDataProcessingOptions normalized = options == null ? new EdgeDataProcessingOptions() : options.Clone();
        normalized.CompressionTolerance = Math.Max(0D, normalized.CompressionTolerance);
        normalized.DownsamplingIntervalMs = ClampMilliseconds(normalized.DownsamplingIntervalMs, 0, 86400000);
        normalized.AlignmentIntervalMs = ClampMilliseconds(normalized.AlignmentIntervalMs, 0, 86400000);
        normalized.FillIntervalMs = ClampMilliseconds(normalized.FillIntervalMs, 0, 86400000);
        normalized.FillMaxGapSeconds = Math.Max(0, Math.Min(86400, normalized.FillMaxGapSeconds));
        normalized.FillMode = NormalizeMode(normalized.FillMode, "Previous");
        normalized.AggregationIntervalSeconds = Math.Max(0, Math.Min(86400, normalized.AggregationIntervalSeconds));
        normalized.AggregationMethods = string.IsNullOrWhiteSpace(normalized.AggregationMethods)
            ? "Average,Min,Max,Count"
            : normalized.AggregationMethods.Trim();
        normalized.MaxSyntheticPointsPerInput = Math.Max(1, Math.Min(10000, normalized.MaxSyntheticPointsPerInput));
        return normalized;
    }

    private static int ClampMilliseconds(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }

    private static string NormalizeMode(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string trimmed = value.Trim();
        if (trimmed.Equals("Linear", StringComparison.OrdinalIgnoreCase))
            return "Linear";
        return "Previous";
    }
}
