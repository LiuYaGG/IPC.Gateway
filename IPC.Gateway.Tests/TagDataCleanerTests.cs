/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：TagDataCleanerTests
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
using IPC.Runtime.Cleaning;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class TagDataCleanerTests
{
    [Fact]
    public void Clean_MarksOutOfRangeQuality()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.OutOfRangeEnabled = true;
            cleaning.MinValue = 0D;
            cleaning.MaxValue = 100D;
        });
        TagValueSnapshot snapshot = CreateSnapshot(120D);

        TagDataCleaner.Clean(snapshot, tag, previous: null);

        Assert.Equal(TagQuality.OutOfRange, snapshot.Quality);
        Assert.True(snapshot.CleaningApplied);
        Assert.Equal("OutOfRange", snapshot.CleaningAction);
        Assert.Equal(120D, Convert.ToDouble(snapshot.Value));
    }

    [Fact]
    public void Clean_DeadbandPreservesPreviousValue()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.DeadbandEnabled = true;
            cleaning.Deadband = 1D;
            cleaning.PreserveLastGoodOnFilter = true;
        });
        TagValueSnapshot previous = CreateSnapshot(10D);
        TagValueSnapshot snapshot = CreateSnapshot(10.5D);

        TagDataCleaner.Clean(snapshot, tag, previous);

        Assert.Equal(TagQuality.Filtered, snapshot.Quality);
        Assert.Equal("DeadbandFiltered", snapshot.CleaningAction);
        Assert.Equal(10D, Convert.ToDouble(snapshot.Value));
    }

    [Fact]
    public void Clean_DuplicatePreservesPreviousValue()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.DuplicateFilterEnabled = true;
            cleaning.PreserveLastGoodOnFilter = true;
        });
        TagValueSnapshot previous = CreateSnapshot(42D);
        TagValueSnapshot snapshot = CreateSnapshot(42D);

        TagDataCleaner.Clean(snapshot, tag, previous);

        Assert.Equal(TagQuality.Filtered, snapshot.Quality);
        Assert.Equal("DuplicateFiltered", snapshot.CleaningAction);
        Assert.Equal(42D, Convert.ToDouble(snapshot.Value));
    }

    [Fact]
    public void Clean_SpikePreservesPreviousValue()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.SpikeFilterEnabled = true;
            cleaning.SpikeThreshold = 20D;
            cleaning.SpikeWindowSeconds = 10;
            cleaning.PreserveLastGoodOnFilter = true;
        });
        TagValueSnapshot previous = CreateSnapshot(20D, DateTime.UtcNow.AddSeconds(-1));
        TagValueSnapshot snapshot = CreateSnapshot(100D, DateTime.UtcNow);

        TagDataCleaner.Clean(snapshot, tag, previous);

        Assert.Equal(TagQuality.Spike, snapshot.Quality);
        Assert.Equal("SpikeFiltered", snapshot.CleaningAction);
        Assert.Equal(20D, Convert.ToDouble(snapshot.Value));
    }

    [Fact]
    public void Clean_EnumMappingReplacesValueText()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.EnumMappingEnabled = true;
            cleaning.EnumMappings.Add(new DataCleaningEnumMappingConfig
            {
                RawValue = "1",
                CleanValue = "Running",
                Description = "Motor running"
            });
        });
        TagValueSnapshot snapshot = CreateSnapshot(1D);

        TagDataCleaner.Clean(snapshot, tag, previous: null);

        Assert.Equal("Running", snapshot.Value);
        Assert.Equal("Running", snapshot.ValueText);
        Assert.Equal("EnumMapped", snapshot.CleaningAction);
    }

    [Fact]
    public void Clean_UnitConversionUpdatesCleanValueAndUnit()
    {
        TagConfig tag = CreateTag(cleaning =>
        {
            cleaning.UnitConversionEnabled = true;
            cleaning.SourceUnit = "m";
            cleaning.TargetUnit = "cm";
            cleaning.UnitMultiplier = 100D;
        });
        TagValueSnapshot snapshot = CreateSnapshot(10D);
        snapshot.Unit = "m";

        TagDataCleaner.Clean(snapshot, tag, previous: null);

        Assert.Equal(1000D, Convert.ToDouble(snapshot.Value));
        Assert.Equal("1000", snapshot.ValueText);
        Assert.Equal("cm", snapshot.Unit);
        Assert.Equal("UnitConverted", snapshot.CleaningAction);
    }

    private static TagConfig CreateTag(Action<DataCleaningConfig> configure)
    {
        TagConfig tag = new TagConfig();
        tag.Cleaning.Enabled = true;
        tag.Cleaning.UnitMultiplier = 1D;
        configure(tag.Cleaning);
        return tag;
    }

    private static TagValueSnapshot CreateSnapshot(double value)
    {
        return CreateSnapshot(value, DateTime.UtcNow);
    }

    private static TagValueSnapshot CreateSnapshot(double value, DateTime timestamp)
    {
        return new TagValueSnapshot
        {
            RawValue = value,
            RawValueText = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Value = value,
            ValueText = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Quality = TagQuality.Good,
            Timestamp = timestamp
        };
    }
}
