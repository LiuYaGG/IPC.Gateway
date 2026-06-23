/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Resilience
* 项目描述 ：
* 类 名 称 ：CircuitBreakerOptions
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

public sealed class CircuitBreakerOptions
{
    public CircuitBreakerOptions()
    {
        Enabled = true;
        FailureThreshold = 5;
        SuccessThreshold = 2;
        BreakDurationSeconds = 30;
        DegradedMode = "Skip";
    }

    public bool Enabled { get; set; }
    public int FailureThreshold { get; set; }
    public int SuccessThreshold { get; set; }
    public int BreakDurationSeconds { get; set; }
    public string DegradedMode { get; set; }

    public CircuitBreakerOptions Clone()
    {
        return new CircuitBreakerOptions
        {
            Enabled = Enabled,
            FailureThreshold = FailureThreshold,
            SuccessThreshold = SuccessThreshold,
            BreakDurationSeconds = BreakDurationSeconds,
            DegradedMode = DegradedMode
        };
    }

    public CircuitBreakerOptions Normalize()
    {
        return new CircuitBreakerOptions
        {
            Enabled = Enabled,
            FailureThreshold = Clamp(FailureThreshold, 1, 100000),
            SuccessThreshold = Clamp(SuccessThreshold, 1, 100000),
            BreakDurationSeconds = Clamp(BreakDurationSeconds, 1, 86400),
            DegradedMode = string.IsNullOrWhiteSpace(DegradedMode) ? "Skip" : DegradedMode.Trim()
        };
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
