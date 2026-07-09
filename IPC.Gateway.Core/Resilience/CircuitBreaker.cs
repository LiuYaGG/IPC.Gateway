/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Resilience
* 项目描述 ：
* 类 名 称 ：CircuitBreaker
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

public sealed class CircuitBreaker
{
    private readonly object _syncRoot = new object();
    private readonly string _name;
    private readonly CircuitBreakerOptions _options;
    private string _state;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private long _totalFailures;
    private long _totalSuccesses;
    private long _totalTrips;
    private long _totalRejected;
    private DateTime _openedUtc;
    private DateTime _nextRetryUtc;
    private DateTime _lastFailureUtc;
    private string _lastFailureMessage;

    public CircuitBreaker(string name, CircuitBreakerOptions options)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "CircuitBreaker" : name.Trim();
        _options = (options ?? new CircuitBreakerOptions()).Normalize();
        _state = "Closed";
        _lastFailureMessage = string.Empty;
    }

    public bool CanExecute()
    {
        return CanExecute(DateTime.UtcNow);
    }

    public bool CanExecute(DateTime nowUtc)
    {
        if (!_options.Enabled)
            return true;

        lock (_syncRoot)
        {
            if (_state == "Open")
            {
                if (nowUtc >= _nextRetryUtc)
                {
                    _state = "HalfOpen";
                    _consecutiveSuccesses = 0;
                    return true;
                }

                _totalRejected++;
                return false;
            }

            return true;
        }
    }

    public void RecordSuccess()
    {
        if (!_options.Enabled)
            return;

        lock (_syncRoot)
        {
            _totalSuccesses++;
            _consecutiveFailures = 0;
            _consecutiveSuccesses++;

            if (_state == "HalfOpen" && _consecutiveSuccesses >= _options.SuccessThreshold)
            {
                _state = "Closed";
                _consecutiveSuccesses = 0;
            }
        }
    }

    public void RecordFailure(string message)
    {
        if (!_options.Enabled)
            return;

        lock (_syncRoot)
        {
            _totalFailures++;
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;
            _lastFailureUtc = DateTime.UtcNow;
            _lastFailureMessage = message ?? string.Empty;

            if (_state == "HalfOpen" || _consecutiveFailures >= _options.FailureThreshold)
                TripNoLock();
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _state = "Closed";
            _consecutiveFailures = 0;
            _consecutiveSuccesses = 0;
            _openedUtc = DateTime.MinValue;
            _nextRetryUtc = DateTime.MinValue;
            _lastFailureUtc = DateTime.MinValue;
            _lastFailureMessage = string.Empty;
        }
    }

    public CircuitBreakerStatus Snapshot()
    {
        lock (_syncRoot)
        {
            return new CircuitBreakerStatus
            {
                Name = _name,
                Enabled = _options.Enabled,
                State = _state,
                IsOpen = _state == "Open",
                IsHalfOpen = _state == "HalfOpen",
                ConsecutiveFailures = _consecutiveFailures,
                ConsecutiveSuccesses = _consecutiveSuccesses,
                TotalFailures = _totalFailures,
                TotalSuccesses = _totalSuccesses,
                TotalTrips = _totalTrips,
                TotalRejected = _totalRejected,
                OpenedTime = ToLocal(_openedUtc),
                NextRetryTime = ToLocal(_nextRetryUtc),
                LastFailureTime = ToLocal(_lastFailureUtc),
                LastFailureMessage = _lastFailureMessage,
                DegradedMode = _options.DegradedMode
            };
        }
    }

    private void TripNoLock()
    {
        _state = "Open";
        _openedUtc = DateTime.UtcNow;
        _nextRetryUtc = _openedUtc.AddSeconds(_options.BreakDurationSeconds);
        _totalTrips++;
    }

    private static DateTime ToLocal(DateTime value)
    {
        if (value == DateTime.MinValue)
            return DateTime.MinValue;
        return value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
