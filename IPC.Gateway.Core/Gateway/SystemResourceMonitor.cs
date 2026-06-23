/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：SystemResourceStatus
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
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace IPC.Gateway.Core.Gateway;

public sealed class SystemResourceStatus
{
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public long ProcessWorkingSetBytes { get; set; }
    public DateTime SampleTime { get; set; } = DateTime.Now;
    public string Source { get; set; } = string.Empty;
}

internal sealed class SystemResourceMonitor
{
    private readonly object _syncRoot = new object();
    private CpuSample _lastCpuSample;

    public SystemResourceStatus Capture()
    {
        lock (_syncRoot)
        {
            SystemResourceStatus status = CaptureMemory();
            status.CpuUsagePercent = CaptureCpuUsagePercent();
            status.ProcessWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64;
            status.SampleTime = DateTime.Now;
            return status;
        }
    }

    private double CaptureCpuUsagePercent()
    {
        CpuSample current;
        if (!TryReadCpuSample(out current))
            return 0D;

        CpuSample previous = _lastCpuSample;
        _lastCpuSample = current;
        if (!previous.IsValid)
            return 0D;

        ulong totalDelta = current.Total - previous.Total;
        ulong idleDelta = current.Idle - previous.Idle;
        if (totalDelta == 0)
            return 0D;

        double usage = (totalDelta - idleDelta) * 100D / totalDelta;
        return Math.Round(Math.Clamp(usage, 0D, 100D), 1);
    }

    private static bool TryReadCpuSample(out CpuSample sample)
    {
        if (OperatingSystem.IsWindows())
            return TryReadWindowsCpuSample(out sample);

        return TryReadProcStatCpuSample(out sample);
    }

    private static bool TryReadWindowsCpuSample(out CpuSample sample)
    {
        sample = default;
        if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            return false;

        ulong idle = idleTime.ToUInt64();
        ulong kernel = kernelTime.ToUInt64();
        ulong user = userTime.ToUInt64();
        sample = new CpuSample(idle, kernel + user);
        return true;
    }

    private static bool TryReadProcStatCpuSample(out CpuSample sample)
    {
        sample = default;
        const string statPath = "/proc/stat";
        if (!File.Exists(statPath))
            return false;

        string? line = File.ReadLines(statPath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("cpu ", StringComparison.Ordinal))
            return false;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return false;

        ulong[] values = new ulong[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
        {
            if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i - 1]))
                return false;
        }

        ulong idle = values.Length > 4 ? values[3] + values[4] : values[3];
        ulong total = 0;
        for (int i = 0; i < values.Length; i++)
            total += values[i];

        sample = new CpuSample(idle, total);
        return true;
    }

    private static SystemResourceStatus CaptureMemory()
    {
        if (OperatingSystem.IsWindows() && TryReadWindowsMemory(out SystemResourceStatus windowsStatus))
            return windowsStatus;

        if (TryReadProcMemInfo(out SystemResourceStatus procStatus))
            return procStatus;

        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        long total = Math.Max(gc.TotalAvailableMemoryBytes, 0);
        long used = GC.GetTotalMemory(false);
        return new SystemResourceStatus
        {
            Source = "GC",
            TotalMemoryBytes = total,
            UsedMemoryBytes = used,
            AvailableMemoryBytes = Math.Max(total - used, 0),
            MemoryUsagePercent = total <= 0 ? 0D : Math.Round(used * 100D / total, 1)
        };
    }

    private static bool TryReadWindowsMemory(out SystemResourceStatus status)
    {
        status = new SystemResourceStatus();
        MemoryStatusEx memory = new MemoryStatusEx();
        memory.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        if (!GlobalMemoryStatusEx(ref memory))
            return false;

        long total = SafeToInt64(memory.ullTotalPhys);
        long available = SafeToInt64(memory.ullAvailPhys);
        status = new SystemResourceStatus
        {
            Source = "Windows",
            TotalMemoryBytes = total,
            AvailableMemoryBytes = available,
            UsedMemoryBytes = Math.Max(total - available, 0),
            MemoryUsagePercent = Math.Round((double)memory.dwMemoryLoad, 1)
        };
        return true;
    }

    private static bool TryReadProcMemInfo(out SystemResourceStatus status)
    {
        status = new SystemResourceStatus();
        const string memInfoPath = "/proc/meminfo";
        if (!File.Exists(memInfoPath))
            return false;

        long total = 0;
        long available = 0;
        foreach (string line in File.ReadLines(memInfoPath))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                total = ParseMemInfoKilobytes(line) * 1024;
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                available = ParseMemInfoKilobytes(line) * 1024;
        }

        if (total <= 0)
            return false;

        status = new SystemResourceStatus
        {
            Source = "ProcFs",
            TotalMemoryBytes = total,
            AvailableMemoryBytes = Math.Max(available, 0),
            UsedMemoryBytes = Math.Max(total - available, 0),
            MemoryUsagePercent = Math.Round(Math.Max(total - available, 0) * 100D / total, 1)
        };
        return true;
    }

    private static long ParseMemInfoKilobytes(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return 0;
        return long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
    }

    private static long SafeToInt64(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public ulong ToUInt64()
        {
            return ((ulong)HighDateTime << 32) | LowDateTime;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private readonly struct CpuSample
    {
        public CpuSample(ulong idle, ulong total)
        {
            Idle = idle;
            Total = total;
            IsValid = true;
        }

        public ulong Idle { get; }
        public ulong Total { get; }
        public bool IsValid { get; }
    }
}
