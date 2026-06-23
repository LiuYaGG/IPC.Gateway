/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeErrorTimelineTests
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
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class RuntimeErrorTimelineTests
{
    [Fact]
    public void GetRecent_ReturnsNewestEventsWithinCapacity()
    {
        RuntimeErrorTimeline timeline = new RuntimeErrorTimeline(2);

        timeline.Add(CreateError("first", new DateTime(2026, 1, 1, 0, 0, 0)));
        timeline.Add(CreateError("second", new DateTime(2026, 1, 1, 0, 1, 0)));
        timeline.Add(CreateError("third", new DateTime(2026, 1, 1, 0, 2, 0)));

        IList<RuntimeErrorDetail> recent = timeline.GetRecent(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("third", recent[0].Message);
        Assert.Equal("second", recent[1].Message);
    }

    [Fact]
    public void Add_ClonesInputAndOutputEvents()
    {
        RuntimeErrorTimeline timeline = new RuntimeErrorTimeline(10);
        RuntimeErrorDetail detail = CreateError("original", new DateTime(2026, 1, 1, 0, 0, 0));

        timeline.Add(detail);
        detail.Message = "mutated";

        IList<RuntimeErrorDetail> firstRead = timeline.GetRecent(1);
        firstRead[0].Message = "reader-mutated";
        IList<RuntimeErrorDetail> secondRead = timeline.GetRecent(1);

        Assert.Equal("original", firstRead[0].Message == "reader-mutated" ? secondRead[0].Message : firstRead[0].Message);
    }

    [Fact]
    public void Add_IgnoresEmptyMessages()
    {
        RuntimeErrorTimeline timeline = new RuntimeErrorTimeline(10);

        timeline.Add(CreateError(string.Empty, DateTime.Now));

        Assert.Empty(timeline.GetRecent(10));
    }

    private static RuntimeErrorDetail CreateError(string message, DateTime timestamp)
    {
        return new RuntimeErrorDetail
        {
            Category = "DeviceConnectionFailure",
            DeviceName = "DeviceA",
            Message = message,
            Source = "Test",
            Timestamp = timestamp
        };
    }
}
