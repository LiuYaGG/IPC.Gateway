/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Resilience
* 项目描述 ：
* 类 名 称 ：CircuitBreakerStatus
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Resilience
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
using System;

namespace IPC.Gateway.Core.Resilience;

public sealed class CircuitBreakerStatus
{
    public CircuitBreakerStatus()
    {
        Name = string.Empty;
        State = "Closed";
        LastFailureMessage = string.Empty;
        DegradedMode = string.Empty;
    }

    public string Name { get; set; }
    public bool Enabled { get; set; }
    public string State { get; set; }
    public bool IsOpen { get; set; }
    public bool IsHalfOpen { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public long TotalFailures { get; set; }
    public long TotalSuccesses { get; set; }
    public long TotalTrips { get; set; }
    public long TotalRejected { get; set; }
    public DateTime OpenedTime { get; set; }
    public DateTime NextRetryTime { get; set; }
    public DateTime LastFailureTime { get; set; }
    public string LastFailureMessage { get; set; }
    public string DegradedMode { get; set; }
}
