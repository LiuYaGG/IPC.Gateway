/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：StorageHealthStatus
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.IO;

namespace IPC.Gateway.Core.Gateway;

public sealed class StorageHealthStatus
{
    public string Path { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = "Healthy";
    public string HealthMessage { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public long UsedBytes { get; set; }
    public double AvailablePercent { get; set; }
    public double UsagePercent { get; set; }
    public long DegradedAvailableBytes { get; set; }
    public long UnhealthyAvailableBytes { get; set; }
    public double DegradedAvailablePercent { get; set; }
    public double UnhealthyAvailablePercent { get; set; }
    public DateTime SampleTime { get; set; } = DateTime.Now;
}

public sealed class StorageHealthThresholds
{
    public StorageHealthThresholds()
    {
        DegradedAvailableBytes = 1024L * 1024L * 1024L;
        UnhealthyAvailableBytes = 256L * 1024L * 1024L;
        DegradedAvailablePercent = 10D;
        UnhealthyAvailablePercent = 2D;
    }

    public long DegradedAvailableBytes { get; set; }
    public long UnhealthyAvailableBytes { get; set; }
    public double DegradedAvailablePercent { get; set; }
    public double UnhealthyAvailablePercent { get; set; }

    public StorageHealthThresholds Clone()
    {
        return new StorageHealthThresholds
        {
            DegradedAvailableBytes = DegradedAvailableBytes,
            UnhealthyAvailableBytes = UnhealthyAvailableBytes,
            DegradedAvailablePercent = DegradedAvailablePercent,
            UnhealthyAvailablePercent = UnhealthyAvailablePercent
        };
    }
}

public static class StorageHealthEvaluator
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";

    public static StorageHealthStatus EvaluatePath(string path)
    {
        return EvaluatePath(path, new StorageHealthThresholds());
    }

    public static StorageHealthStatus EvaluatePath(string path, StorageHealthThresholds thresholds)
    {
        string fullPath = ResolveFullPath(path);
        try
        {
            string rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath))
                return BuildUnavailable(fullPath, string.Empty, thresholds, "Storage root could not be resolved.");

            DriveInfo drive = new DriveInfo(rootPath);
            if (!drive.IsReady)
                return BuildUnavailable(fullPath, rootPath, thresholds, "Storage root is not ready.");

            return Evaluate(fullPath, rootPath, drive.TotalSize, drive.AvailableFreeSpace, thresholds);
        }
        catch (Exception ex)
        {
            return BuildUnavailable(fullPath, string.Empty, thresholds, "Storage status is unavailable: " + ex.Message);
        }
    }

    public static StorageHealthStatus Evaluate(string path, string rootPath, long totalBytes, long availableBytes, StorageHealthThresholds thresholds)
    {
        thresholds = NormalizeThresholds(thresholds);
        StorageHealthStatus status = new StorageHealthStatus
        {
            Path = path ?? string.Empty,
            RootPath = rootPath ?? string.Empty,
            TotalBytes = Math.Max(0, totalBytes),
            AvailableBytes = Math.Max(0, availableBytes),
            DegradedAvailableBytes = thresholds.DegradedAvailableBytes,
            UnhealthyAvailableBytes = thresholds.UnhealthyAvailableBytes,
            DegradedAvailablePercent = thresholds.DegradedAvailablePercent,
            UnhealthyAvailablePercent = thresholds.UnhealthyAvailablePercent,
            SampleTime = DateTime.Now
        };

        if (status.TotalBytes <= 0)
        {
            status.HealthStatus = Degraded;
            status.HealthMessage = "Storage capacity could not be measured.";
            return status;
        }

        if (status.AvailableBytes > status.TotalBytes)
            status.AvailableBytes = status.TotalBytes;

        status.IsAvailable = true;
        status.UsedBytes = Math.Max(status.TotalBytes - status.AvailableBytes, 0);
        status.AvailablePercent = Math.Round(status.AvailableBytes * 100D / status.TotalBytes, 2);
        status.UsagePercent = Math.Round(status.UsedBytes * 100D / status.TotalBytes, 2);

        if (status.AvailableBytes <= thresholds.UnhealthyAvailableBytes ||
            status.AvailablePercent <= thresholds.UnhealthyAvailablePercent)
        {
            status.HealthStatus = Unhealthy;
            status.HealthMessage = "Storage free space is critically low.";
            return status;
        }

        if (status.AvailableBytes <= thresholds.DegradedAvailableBytes ||
            status.AvailablePercent <= thresholds.DegradedAvailablePercent)
        {
            status.HealthStatus = Degraded;
            status.HealthMessage = "Storage free space is low.";
            return status;
        }

        status.HealthStatus = Healthy;
        status.HealthMessage = "Storage free space is healthy.";
        return status;
    }

    private static StorageHealthStatus BuildUnavailable(string path, string rootPath, StorageHealthThresholds thresholds, string message)
    {
        thresholds = NormalizeThresholds(thresholds);
        return new StorageHealthStatus
        {
            Path = path ?? string.Empty,
            RootPath = rootPath ?? string.Empty,
            HealthStatus = Unhealthy,
            HealthMessage = message ?? "Storage status is unavailable.",
            IsAvailable = false,
            DegradedAvailableBytes = thresholds.DegradedAvailableBytes,
            UnhealthyAvailableBytes = thresholds.UnhealthyAvailableBytes,
            DegradedAvailablePercent = thresholds.DegradedAvailablePercent,
            UnhealthyAvailablePercent = thresholds.UnhealthyAvailablePercent,
            SampleTime = DateTime.Now
        };
    }

    private static string ResolveFullPath(string path)
    {
        string value = string.IsNullOrWhiteSpace(path) ? AppDomain.CurrentDomain.BaseDirectory : path.Trim();
        return Path.GetFullPath(value);
    }

    public static StorageHealthThresholds NormalizeThresholds(StorageHealthThresholds thresholds)
    {
        StorageHealthThresholds normalized = thresholds?.Clone() ?? new StorageHealthThresholds();
        if (normalized.UnhealthyAvailableBytes < 0)
            normalized.UnhealthyAvailableBytes = 0;
        if (normalized.DegradedAvailableBytes < normalized.UnhealthyAvailableBytes)
            normalized.DegradedAvailableBytes = normalized.UnhealthyAvailableBytes;
        normalized.UnhealthyAvailablePercent = Math.Clamp(normalized.UnhealthyAvailablePercent, 0D, 100D);
        normalized.DegradedAvailablePercent = Math.Clamp(normalized.DegradedAvailablePercent, normalized.UnhealthyAvailablePercent, 100D);
        return normalized;
    }
}
