/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：RuntimeReconnectBackoffCalculator
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

namespace IPC.Runtime.Engine
{
    public static class RuntimeReconnectBackoffCalculator
    {
        public const int DefaultJitterPercent = 10;

        public static int CalculateDelayMs(int consecutiveFailures, int baseDelayMs, int maxDelayMs)
        {
            int safeMaxDelay = NormalizeMaxDelay(maxDelayMs);
            int safeBaseDelay = Clamp(baseDelayMs <= 0 ? 1000 : baseDelayMs, 100, safeMaxDelay);
            if (consecutiveFailures <= 1)
                return safeBaseDelay;

            long delay = safeBaseDelay;
            for (int i = 1; i < consecutiveFailures; i++)
            {
                delay *= 2L;
                if (delay >= safeMaxDelay)
                    return safeMaxDelay;
            }

            return (int)Math.Min(delay, safeMaxDelay);
        }

        public static int CalculateScheduledDelayMs(
            int consecutiveFailures,
            int baseDelayMs,
            int maxDelayMs,
            string jitterKey)
        {
            return CalculateScheduledDelayMs(
                consecutiveFailures,
                baseDelayMs,
                maxDelayMs,
                jitterKey,
                DefaultJitterPercent);
        }

        public static int CalculateScheduledDelayMs(
            int consecutiveFailures,
            int baseDelayMs,
            int maxDelayMs,
            string jitterKey,
            int jitterPercent)
        {
            int baseDelay = CalculateDelayMs(consecutiveFailures, baseDelayMs, maxDelayMs);
            int safeMaxDelay = NormalizeMaxDelay(maxDelayMs);
            int safeJitterPercent = Clamp(jitterPercent, 0, 100);
            if (safeJitterPercent <= 0 || baseDelay >= safeMaxDelay)
                return baseDelay;

            long maxJitter = (long)baseDelay * safeJitterPercent / 100L;
            long remainingDelay = safeMaxDelay - baseDelay;
            int cappedMaxJitter = (int)Math.Min(maxJitter, remainingDelay);
            if (cappedMaxJitter <= 0)
                return baseDelay;

            uint hash = ComputeStableHash(jitterKey ?? string.Empty, consecutiveFailures);
            int jitter = (int)(hash % (uint)(cappedMaxJitter + 1));
            return baseDelay + jitter;
        }

        private static int NormalizeMaxDelay(int maxDelayMs)
        {
            return Clamp(maxDelayMs <= 0 ? 30000 : maxDelayMs, 100, 86400000);
        }

        private static uint ComputeStableHash(string value, int salt)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }

                hash ^= (uint)salt;
                hash *= 16777619;
                return hash;
            }
        }

        private static int Clamp(int value, int minValue, int maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }
    }
}
