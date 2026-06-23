/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeEngineLifecycleTests
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
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class RuntimeEngineLifecycleTests
{
    [Fact]
    public void TryGetSnapshot_MissingTagReturnsNullSnapshot()
    {
        using RuntimeEngine engine = new RuntimeEngine(CreateQuietSchedulerOptions());

        bool found = engine.TryGetSnapshot("Device", "Group", "Tag", out TagValueSnapshot? snapshot);

        Assert.False(found);
        Assert.Null(snapshot);
    }

    [Fact]
    public void StartStop_UpdatesRunningState()
    {
        using RuntimeEngine engine = new RuntimeEngine(CreateQuietSchedulerOptions());

        engine.Start(new ProjectConfig());
        Assert.True(engine.IsRunning);

        engine.Stop();
        Assert.False(engine.IsRunning);
    }

    private static RuntimeSchedulerOptions CreateQuietSchedulerOptions()
    {
        return new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 60000
        };
    }
}
