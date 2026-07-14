/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：DeviceRuntimeState
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Engine
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
using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    
    
    
    
    
    
    
    
    internal enum DeviceRuntimeConfigTransition
    {
        None,
        Disabled,
        Enabled
    }

    
    internal sealed class DeviceRuntimeState
    {
        public DeviceRuntimeState(DeviceConfig config, CircuitBreakerOptions protocolCircuitBreakerOptions)
        {
            Config = config;
            SyncRoot = new object();
            Actor = new DeviceActor(config == null ? string.Empty : config.Name ?? string.Empty);
            ProtocolCircuitBreaker = new CircuitBreaker(
                "ProtocolDriver:" + (config == null ? string.Empty : config.Name ?? string.Empty),
                protocolCircuitBreakerOptions);
            NextPollUtc = DateTime.MinValue;
            NextReconnectUtc = DateTime.MinValue;
            LastReconnectDelayMs = 0;
            LastError = string.Empty;
            LastConnectionError = string.Empty;
            LastConnectionErrorTime = DateTime.MinValue;
            PendingRecoveryConnectionError = string.Empty;
            LastTaskStatus = "Idle";
            LastTaskError = string.Empty;
            StableStatus = config == null || !config.Enabled ? "Disabled" : "Offline";
            PendingStatus = string.Empty;
            PendingStatusSinceUtc = DateTime.MinValue;
            StableStatusChangedUtc = DateTime.UtcNow;
            SubscriptionFingerprint = string.Empty;
            LastSubscriptionError = string.Empty;
            NextSubscriptionRetryUtc = DateTime.MinValue;
            ReadPlan = CompiledDeviceReadPlan.Compile(config);
            DeviceState = config == null || !config.Enabled ? "Disabled" : "Offline";
            RecoveryState = "Idle";
        }

        public DeviceConfig Config { get; private set; }
        public object SyncRoot { get; private set; }
        public DeviceActor Actor { get; private set; }
        public CircuitBreaker ProtocolCircuitBreaker { get; private set; }
        public CompiledDeviceReadPlan ReadPlan { get; private set; }
        public IPlcClient? Client { get; set; }
        public IPlcSubscription? Subscription { get; set; }
        public string SubscriptionFingerprint { get; set; }
        public bool SubscriptionUnavailable { get; set; }
        public string LastSubscriptionError { get; set; }
        public DateTime NextSubscriptionRetryUtc { get; set; }
        public DateTime LastSubscriptionNotificationUtc { get; set; }
        public bool IsPolling { get; set; }
        public bool IsQueued { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime NextPollUtc { get; set; }
        public DateTime NextReconnectUtc { get; set; }
        public int LastReconnectDelayMs { get; set; }
        public string LastError { get; set; }
        public string LastConnectionError { get; set; }
        public DateTime LastConnectionErrorTime { get; set; }
        public bool UnavailableTagsMarked { get; set; }
        public int PendingRecoveryFailureCount { get; set; }
        public string PendingRecoveryConnectionError { get; set; }
        public long TotalReads { get; set; }
        public long SuccessfulReads { get; set; }
        public long FailedReads { get; set; }
        public DateTime LastPollTime { get; set; }
        public DateTime LastSuccessTime { get; set; }
        public DateTime LastFailureTime { get; set; }
        public long CurrentTaskId { get; set; }
        public DateTime CurrentTaskQueuedUtc { get; set; }
        public DateTime CurrentTaskStartedUtc { get; set; }
        public DateTime CurrentTaskFinishedUtc { get; set; }
        public long LastTaskDurationMs { get; set; }
        public string LastTaskStatus { get; set; }
        public string LastTaskError { get; set; }
        public long SlowPollCount { get; set; }
        public long TimeoutCount { get; set; }
        public string StableStatus { get; private set; }
        public string PendingStatus { get; private set; }
        public int PendingStatusCount { get; private set; }
        public DateTime PendingStatusSinceUtc { get; private set; }
        public DateTime StableStatusChangedUtc { get; private set; }
        public string DeviceState { get; set; }
        public bool IsIsolated { get; set; }
        public string RecoveryState { get; set; }
        public DateTime IsolatedSinceUtc { get; set; }
        public DateTime NextRecoveryProbeUtc { get; set; }
        public string LastKnownGoodTagId { get; set; } = string.Empty;

        public DeviceRuntimeConfigTransition ReuseConfig(DeviceConfig config)
        {
            if (config == null)
                return DeviceRuntimeConfigTransition.None;

            bool wasEnabled = Config != null && Config.Enabled;
            bool isEnabled = config.Enabled;
            Config = config;
            ReadPlan = CompiledDeviceReadPlan.Compile(config);
            IsQueued = false;
            if (!IsPolling && string.Equals(LastTaskStatus, "Queued", StringComparison.OrdinalIgnoreCase))
                LastTaskStatus = "Idle";

            if (wasEnabled && !isEnabled)
            {
                ResetConnectivityState("Disabled", DateTime.UtcNow);
                return DeviceRuntimeConfigTransition.Disabled;
            }

            if (!wasEnabled && isEnabled)
            {
                ResetConnectivityState("Offline", DateTime.UtcNow);
                return DeviceRuntimeConfigTransition.Enabled;
            }

            return DeviceRuntimeConfigTransition.None;
        }

        private void ResetConnectivityState(string status, DateTime nowUtc)
        {
            ConsecutiveFailures = 0;
            NextPollUtc = DateTime.MinValue;
            NextReconnectUtc = DateTime.MinValue;
            LastReconnectDelayMs = 0;
            LastError = string.Empty;
            LastConnectionError = string.Empty;
            LastConnectionErrorTime = DateTime.MinValue;
            UnavailableTagsMarked = false;
            DisposeSubscription();
            SubscriptionFingerprint = string.Empty;
            SubscriptionUnavailable = false;
            LastSubscriptionError = string.Empty;
            NextSubscriptionRetryUtc = DateTime.MinValue;
            LastSubscriptionNotificationUtc = DateTime.MinValue;
            PendingRecoveryFailureCount = 0;
            PendingRecoveryConnectionError = string.Empty;
            ProtocolCircuitBreaker.Reset();
            DeviceState = string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : "Offline";
            IsIsolated = false;
            RecoveryState = "Idle";
            IsolatedSinceUtc = DateTime.MinValue;
            NextRecoveryProbeUtc = DateTime.MinValue;
            LastKnownGoodTagId = string.Empty;
            ForceStatus(status, nowUtc);
        }

        public void DisposeSubscription()
        {
            IPlcSubscription? subscription = Subscription;
            Subscription = null;
            if (subscription == null)
                return;

            try
            {
                subscription.Dispose();
            }
            catch
            {
            }
        }

        public string ApplyStatusSample(
            string candidateStatus,
            DateTime nowUtc,
            int failureDebounceCount,
            int failureDebounceMs,
            int recoveryDebounceCount,
            int recoveryDebounceMs)
        {
            string candidate = NormalizeStatus(candidateStatus);
            if (string.IsNullOrWhiteSpace(StableStatus))
            {
                PromoteStatus(candidate, nowUtc);
                return StableStatus;
            }

            if (string.Equals(candidate, StableStatus, StringComparison.OrdinalIgnoreCase))
            {
                ClearPendingStatus();
                return StableStatus;
            }

            if (ShouldSwitchImmediately(StableStatus, candidate))
            {
                PromoteStatus(candidate, nowUtc);
                return StableStatus;
            }

            if (!string.Equals(candidate, PendingStatus, StringComparison.OrdinalIgnoreCase))
            {
                PendingStatus = candidate;
                PendingStatusCount = 1;
                PendingStatusSinceUtc = nowUtc;
            }
            else
            {
                PendingStatusCount++;
            }

            int requiredCount = IsRecoveryStatus(candidate)
                ? Math.Max(1, recoveryDebounceCount)
                : Math.Max(1, failureDebounceCount);
            int requiredMs = IsRecoveryStatus(candidate)
                ? Math.Max(0, recoveryDebounceMs)
                : Math.Max(0, failureDebounceMs);

            if (PendingStatusCount >= requiredCount && (nowUtc - PendingStatusSinceUtc).TotalMilliseconds >= requiredMs)
                PromoteStatus(candidate, nowUtc);

            return StableStatus;
        }

        public string ForceStatus(string status, DateTime nowUtc)
        {
            PromoteStatus(status, nowUtc);
            return StableStatus;
        }

        private void PromoteStatus(string status, DateTime nowUtc)
        {
            StableStatus = NormalizeStatus(status);
            StableStatusChangedUtc = nowUtc;
            ClearPendingStatus();
        }

        private void ClearPendingStatus()
        {
            PendingStatus = string.Empty;
            PendingStatusCount = 0;
            PendingStatusSinceUtc = DateTime.MinValue;
        }

        private static bool ShouldSwitchImmediately(string currentStatus, string candidateStatus)
        {
            return string.Equals(candidateStatus, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentStatus, "Disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecoveryStatus(string status)
        {
            return string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStatus(string status)
        {
            if (string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase))
                return "Disabled";
            if (string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase))
                return "Online";
            if (string.Equals(status, "Degraded", StringComparison.OrdinalIgnoreCase))
                return "Degraded";
            if (string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase))
                return "Error";
            return "Offline";
        }
    }
}
