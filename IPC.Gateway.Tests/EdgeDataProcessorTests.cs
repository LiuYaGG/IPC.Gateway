/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：EdgeDataProcessorTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Gateway.DataProcessing;

namespace IPC.Gateway.Tests;

public sealed class EdgeDataProcessorTests
{
    [Fact]
    public void Compression_SkipsWithinTolerance()
    {
        EdgeDataProcessor processor = new EdgeDataProcessor(new EdgeDataProcessingOptions
        {
            Enabled = true,
            CompressionEnabled = true,
            CompressionTolerance = 0.5D
        });

        EdgeDataProcessingResult first = processor.Process(Point(10D, 0));
        EdgeDataProcessingResult second = processor.Process(Point(10.2D, 1));

        Assert.True(first.WriteCurrent);
        Assert.False(second.WriteCurrent);
        Assert.True(second.CompressionSkipped);
        Assert.Equal(1, processor.GetStats().CompressedValueCount);
    }

    [Fact]
    public void Downsampling_SkipsInsideInterval()
    {
        EdgeDataProcessor processor = new EdgeDataProcessor(new EdgeDataProcessingOptions
        {
            Enabled = true,
            DownsamplingEnabled = true,
            DownsamplingIntervalMs = 1000
        });

        processor.Process(Point(10D, 0));
        EdgeDataProcessingResult second = processor.Process(Point(11D, 0.5D));
        EdgeDataProcessingResult third = processor.Process(Point(12D, 1.1D));

        Assert.False(second.WriteCurrent);
        Assert.True(second.DownsamplingSkipped);
        Assert.True(third.WriteCurrent);
        Assert.Equal(1, processor.GetStats().DownsampledValueCount);
    }

    [Fact]
    public void Alignment_AlignsTimestampToGrid()
    {
        EdgeDataProcessor processor = new EdgeDataProcessor(new EdgeDataProcessingOptions
        {
            Enabled = true,
            AlignmentEnabled = true,
            AlignmentIntervalMs = 1000
        });

        EdgeDataProcessingResult result = processor.Process(Point(10D, 1.75D));

        Assert.True(result.WriteCurrent);
        Assert.Equal("aligned", result.Current!.ProcessingType);
        Assert.Equal(BaseTime.AddSeconds(1), result.Current.Point.Timestamp);
        Assert.Equal(BaseTime.AddMilliseconds(1750), result.Current.OriginalTimestamp);
    }

    [Fact]
    public void Fill_GeneratesMissingAlignedPoints()
    {
        EdgeDataProcessor processor = new EdgeDataProcessor(new EdgeDataProcessingOptions
        {
            Enabled = true,
            AlignmentEnabled = true,
            AlignmentIntervalMs = 1000,
            FillEnabled = true,
            FillIntervalMs = 1000,
            FillMaxGapSeconds = 10
        });

        processor.Process(Point(10D, 0));
        EdgeDataProcessingResult result = processor.Process(Point(13D, 3));

        Assert.Equal(2, result.DerivedPoints.Count);
        Assert.All(result.DerivedPoints, item => Assert.Equal("fill", item.ProcessingType));
        Assert.Equal(BaseTime.AddSeconds(1), result.DerivedPoints[0].Point.Timestamp);
        Assert.Equal("10", result.DerivedPoints[0].Point.ValueText);
        Assert.Equal(2, processor.GetStats().FilledValueCount);
    }

    [Fact]
    public void Aggregation_EmitsClosedWindowStatistics()
    {
        EdgeDataProcessor processor = new EdgeDataProcessor(new EdgeDataProcessingOptions
        {
            Enabled = true,
            AggregationEnabled = true,
            AggregationIntervalSeconds = 10,
            AggregationMethods = "Average,Min,Max,Count"
        });

        processor.Process(Point(10D, 1));
        processor.Process(Point(20D, 2));
        EdgeDataProcessingResult result = processor.Process(Point(40D, 11));

        Assert.Equal(4, result.DerivedPoints.Count);
        Assert.Contains(result.DerivedPoints, item => item.AggregateMethod == "Average" && item.Point.ValueText == "15");
        Assert.Contains(result.DerivedPoints, item => item.AggregateMethod == "Min" && item.Point.ValueText == "10");
        Assert.Contains(result.DerivedPoints, item => item.AggregateMethod == "Max" && item.Point.ValueText == "20");
        Assert.Contains(result.DerivedPoints, item => item.AggregateMethod == "Count" && item.Point.ValueText == "2");
        Assert.Equal(4, processor.GetStats().AggregatedValueCount);
    }

    private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local);

    private static EdgeDataPoint Point(double value, double seconds)
    {
        return new EdgeDataPoint
        {
            TagKey = "Line1.Press.Temperature",
            Timestamp = BaseTime.AddMilliseconds(seconds * 1000D),
            ValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            RawValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Quality = "Good",
            Unit = "C",
            HasNumericValue = true,
            NumericValue = value
        };
    }
}
