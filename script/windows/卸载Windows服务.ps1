[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ServiceName = "IPCGateway",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if (-not $WhatIfPreference) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '请以管理员身份运行 PowerShell，再卸载 Windows 服务。'
    }
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Windows service '$ServiceName' is not installed."
    return
}

if ($service.Status -ne "Stopped") {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop Windows service")) {
        $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
        $serviceProcess = $null
        if ($serviceInfo.ProcessId -gt 0) {
            try { $serviceProcess = [System.Diagnostics.Process]::GetProcessById([int]$serviceInfo.ProcessId) } catch { }
        }
        Stop-Service -Name $ServiceName -Force:$Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
        if ($serviceProcess) {
            try {
                if (-not $serviceProcess.WaitForExit(60000)) {
                    throw "The service process has not exited yet. Wait for it to release program files before updating."
                }
            }
            finally { $serviceProcess.Dispose() }
        }
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Delete Windows service")) {
    $service.Dispose()
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Deleting service '$ServiceName' failed with code $LASTEXITCODE."
    }
}

Write-Host "Windows service '$ServiceName' has been removed."
Write-Host 'Program files, configuration, databases and data directories were retained.'
