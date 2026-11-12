<#
.SYNOPSIS
每秒向 Snap7 serverdemo 的 DB1、DB2、DB3 写入随机测试数据。
.DESCRIPTION
默认 Pressure1500 模式匹配仓库之前生成的每块 500 点压测布局，保留前 128 字节及未使用的空隙。
Raw 模式按 StartByte/ByteCount 写随机字节，不保证浮点、字符串等类型的有效性。
不读取配置数据库，不依赖网关安装目录；需要与 PowerShell 位数一致的 snap7.dll。
#>
[CmdletBinding()]
param(
    [string]$IpAddress = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 102,
    [ValidateRange(0, 7)][int]$Rack = 0,
    [ValidateRange(0, 31)][int]$Slot = 0,
    [ValidateSet('Basic', 'PG', 'OP')][string]$ConnectionType = 'Basic',
    [ValidateRange(100, 3600000)][int]$IntervalMs = 1000,
    [ValidateRange(100, 60000)][int]$TimeoutMs = 1500,
    [ValidateSet('Pressure1500', 'Raw')][string]$Mode = 'Pressure1500',
    [ValidateRange(0, 2000000)][int]$StartByte = 0,
    [ValidateRange(1, 65536)][int]$ByteCount = 2048,
    [string]$Snap7Dll = '',
    [ValidateRange(0, 2147483647)][int]$Cycles = 0,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw '此脚本使用 Windows snap7.dll，请在 Windows PowerShell 中运行。' }
if (-not ('IpcGateway.Snap7Refresh.Client' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace IpcGateway.Snap7Refresh {
    public sealed class Client : IDisposable {
        private IntPtr handle;
        private static IntPtr library;
        private static string loadedPath;
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern IntPtr Cli_Create();
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern void Cli_Destroy(ref IntPtr client);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern int Cli_Disconnect(IntPtr client);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall, CharSet=CharSet.Ansi)] private static extern int Cli_ConnectTo(IntPtr client, string ip, int rack, int slot);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern int Cli_SetConnectionType(IntPtr client, ushort type);
        [DllImport("snap7.dll", EntryPoint="Cli_SetParam", CallingConvention=CallingConvention.StdCall)] private static extern int SetWord(IntPtr client, int number, ref ushort value);
        [DllImport("snap7.dll", EntryPoint="Cli_SetParam", CallingConvention=CallingConvention.StdCall)] private static extern int SetInt(IntPtr client, int number, ref int value);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern int Cli_DBRead(IntPtr client, int db, int start, int size, [Out] byte[] buffer);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall)] private static extern int Cli_DBWrite(IntPtr client, int db, int start, int size, [In] byte[] buffer);
        [DllImport("snap7.dll", CallingConvention=CallingConvention.StdCall, CharSet=CharSet.Ansi)] private static extern int Cli_ErrorText(int error, StringBuilder text, int size);
        private static void Check(int code) {
            if (code == 0) return;
            var text = new StringBuilder(1024); Cli_ErrorText(code, text, text.Capacity);
            throw new InvalidOperationException(String.Format("Snap7 0x{0:X8}: {1}", code, text));
        }
        public Client(string dllPath, ushort port, ushort type, int timeout) {
            string fullPath = Path.GetFullPath(dllPath);
            if (library != IntPtr.Zero && !String.Equals(fullPath, loadedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Another snap7.dll is already loaded. Open a new PowerShell window.");
            if (library == IntPtr.Zero) {
                library = LoadLibraryEx(fullPath, IntPtr.Zero, 8);
                if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load snap7.dll. Check the DLL and PowerShell architecture (32/64 bit).");
                loadedPath = fullPath;
            }
            handle = Cli_Create();
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Snap7 client creation failed.");
            try {
                Check(SetWord(handle, 2, ref port)); Check(Cli_SetConnectionType(handle, type));
                Check(SetInt(handle, 3, ref timeout)); Check(SetInt(handle, 4, ref timeout)); Check(SetInt(handle, 5, ref timeout));
            } catch { Dispose(); throw; }
        }
        public void Connect(string ip, int rack, int slot) { Check(Cli_ConnectTo(handle, ip, rack, slot)); }
        public void Disconnect() { if (handle != IntPtr.Zero) Cli_Disconnect(handle); }
        public byte[] Read(int db, int start, int count) { var bytes = new byte[count]; Check(Cli_DBRead(handle, db, start, count, bytes)); return bytes; }
        public void Write(int db, int start, byte[] bytes) { Check(Cli_DBWrite(handle, db, start, bytes.Length, bytes)); }
        public void Dispose() { if (handle != IntPtr.Zero) { Cli_Disconnect(handle); Cli_Destroy(ref handle); } }
    }
    public static class Data {
        private static void Put(byte[] target, int offset, byte[] value) {
            if (BitConverter.IsLittleEndian) Array.Reverse(value);
            Buffer.BlockCopy(value, 0, target, offset, value.Length);
        }
        private static void Series(byte[] bytes, Random random, string type, int start, int count) {
            for (int i=0; i<count; i++) {
                switch(type) {
                    case "Int16": Put(bytes, start+i*2, BitConverter.GetBytes((short)random.Next(-30000,30001))); break;
                    case "UInt16": Put(bytes, start+i*2, BitConverter.GetBytes((ushort)random.Next(60001))); break;
                    case "Int32": Put(bytes, start+i*4, BitConverter.GetBytes(random.Next(-1000000,1000001))); break;
                    case "UInt32": Put(bytes, start+i*4, BitConverter.GetBytes((uint)random.Next(2000001))); break;
                    case "Int64": Put(bytes, start+i*8, BitConverter.GetBytes((long)random.Next(-1000000000,1000000001))); break;
                    case "UInt64": Put(bytes, start+i*8, BitConverter.GetBytes((ulong)random.Next(2000000001))); break;
                    case "Float": Put(bytes, start+i*4, BitConverter.GetBytes((float)Math.Round(random.NextDouble()*2000-1000,2))); break;
                    case "Double": Put(bytes, start+i*8, BitConverter.GetBytes(Math.Round(random.NextDouble()*20000-10000,3))); break;
                }
            }
        }
        public static byte[] PressureImage(Random random) {
            var bytes = new byte[1768];
            var flags = new byte[25]; random.NextBytes(flags); Buffer.BlockCopy(flags,0,bytes,128,25);
            var octets = new byte[40]; random.NextBytes(octets); Buffer.BlockCopy(octets,0,bytes,160,40);
            Series(bytes,random,"Int16",208,40); Series(bytes,random,"UInt16",288,40);
            Series(bytes,random,"Int32",368,30); Series(bytes,random,"UInt32",488,30);
            Series(bytes,random,"Int64",608,20); Series(bytes,random,"UInt64",768,20);
            Series(bytes,random,"Float",928,40); Series(bytes,random,"Double",1088,20);
            var arrayBytes = new byte[40]; random.NextBytes(arrayBytes); Buffer.BlockCopy(arrayBytes,0,bytes,1280,40);
            Series(bytes,random,"Int16",1320,16); Series(bytes,random,"UInt16",1352,16);
            Series(bytes,random,"Int32",1384,16); Series(bytes,random,"UInt32",1448,16);
            Series(bytes,random,"Int64",1512,8); Series(bytes,random,"UInt64",1576,8);
            Series(bytes,random,"Float",1640,16); Series(bytes,random,"Double",1704,8);
            return bytes;
        }
        public static byte[] Slice(byte[] bytes, int start, int count) {
            var result = new byte[count]; Buffer.BlockCopy(bytes,start,result,0,count); return result;
        }
    }
}
'@
}

# Pressure1500: only the occupied byte spans; the original 0..127 and gaps stay untouched.
$ranges = if ($Mode -eq 'Pressure1500') {
    @(@{ Start = 128; Count = 25 }, @{ Start = 160; Count = 40 },
      @{ Start = 208; Count = 1040 }, @{ Start = 1280; Count = 488 })
} else { @(@{ Start = $StartByte; Count = $ByteCount }) }
$lastByte = ($ranges | ForEach-Object { $_.Start + $_.Count - 1 } | Measure-Object -Maximum).Maximum
$rangeText = ($ranges | ForEach-Object { '{0}..{1}' -f $_.Start, ($_.Start + $_.Count - 1) }) -join ', '
Write-Host ('目标 {0}:{1}；DB1、DB2、DB3；模式 {2}；间隔 {3} ms' -f $IpAddress, $Port, $Mode, $IntervalMs)
Write-Host ('每个 DB 刷新字节范围：{0}' -f $rangeText)
if ($CheckOnly) { Write-Host '布局检查通过，未连接、未写入。'; return }

if ([string]::IsNullOrWhiteSpace($Snap7Dll)) {
    $candidates = @((Join-Path $PSScriptRoot 'snap7.dll'), (Join-Path (Get-Location).Path 'snap7.dll'),
        (Join-Path $PSScriptRoot '../../tmp/s7-validation/snap7-wheel/snap7/lib/snap7.dll'))
    foreach ($process in @(Get-Process -Name serverdemo -ErrorAction SilentlyContinue)) {
        if ($process.Path) { $candidates += Join-Path (Split-Path $process.Path) 'snap7.dll' }
    }
    $Snap7Dll = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $Snap7Dll -or -not (Test-Path -LiteralPath $Snap7Dll -PathType Leaf)) {
    throw '未找到 snap7.dll。请放到脚本旁边，或者通过 -Snap7Dll 指定完整路径；DLL 位数须与 PowerShell 一致。'
}
$Snap7Dll = (Resolve-Path -LiteralPath $Snap7Dll).Path
if ([IO.Path]::GetFileName($Snap7Dll) -ine 'snap7.dll') { throw '指定的库文件名必须为 snap7.dll。' }
$connectionCode = @{ Basic = 3; PG = 1; OP = 2 }[$ConnectionType]
$client = New-Object IpcGateway.Snap7Refresh.Client($Snap7Dll, [uint16]$Port, [uint16]$connectionCode, $TimeoutMs)
$random = New-Object Random
$connected = $false
$validatedDbs = New-Object 'System.Collections.Generic.HashSet[int]'
$successful = 0L
$attempt = 0L
Write-Host ('使用 {0} 位 PowerShell，DLL：{1}' -f ([IntPtr]::Size * 8), $Snap7Dll)
Write-Host '开始刷新。按 Ctrl+C 或关闭窗口停止；serverdemo 断开后自动重试。'
try {
    while ($Cycles -eq 0 -or $attempt -lt $Cycles) {
        $attempt++
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $writtenDbs = @()
        foreach ($db in 1, 2, 3) {
            try {
                if (-not $connected) {
                    $client.Connect($IpAddress, $Rack, $Slot)
                    $connected = $true
                    $validatedDbs.Clear()
                }
                if (-not $validatedDbs.Contains($db)) {
                    # Probe the last byte before changing this DB, avoiding out-of-range partial cycles.
                    $null = $client.Read($db, [int]$lastByte, 1)
                    $null = $validatedDbs.Add($db)
                }
                if ($Mode -eq 'Pressure1500') {
                    $image = [IpcGateway.Snap7Refresh.Data]::PressureImage($random)
                    foreach ($range in $ranges) {
                        $client.Write($db, $range.Start, [IpcGateway.Snap7Refresh.Data]::Slice($image, $range.Start, $range.Count))
                    }
                } else {
                    $bytes = New-Object byte[] $ByteCount
                    $random.NextBytes($bytes)
                    $client.Write($db, $StartByte, $bytes)
                }
                $writtenDbs += "DB$db"
            } catch {
                Write-Warning ('{0:HH:mm:ss} DB{1} 刷新失败：{2}' -f (Get-Date), $db, $_.Exception.Message)
                $client.Disconnect()
                $connected = $false
            }
        }
        if ($writtenDbs.Count -eq 3) { $successful++ }
        Write-Host ('{0:HH:mm:ss} 第 {1} 轮，已写入：{2}；完整成功 {3} 轮；耗时 {4} ms' -f (Get-Date), $attempt, ($writtenDbs -join ', '), $successful, $clock.ElapsedMilliseconds)
        if ($Cycles -gt 0 -and $attempt -ge $Cycles) { break }
        # No catch-up queue: a slow cycle starts the next one only after completion.
        $waitMs = [Math]::Max(0, $IntervalMs - $clock.ElapsedMilliseconds)
        if ($waitMs -gt 0) { Start-Sleep -Milliseconds ([int]$waitMs) }
    }
} finally { $client.Dispose() }
if ($Cycles -gt 0 -and $successful -ne $attempt) { throw '部分刷新失败，请检查上面的 DB、大小和连接错误。' }
