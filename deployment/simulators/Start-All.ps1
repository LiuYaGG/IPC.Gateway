$ErrorActionPreference = 'Stop'
$root = 'D:\IPC-Simulators'
$taskName = 'IPC Protocol Simulators'

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 4
} else {
    $python = Join-Path $root 'Python\python.exe'
    $hostScript = Join-Path $root 'simulator_host.py'
    [Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
    Start-Process -FilePath $python -ArgumentList ('"' + $hostScript + '"') -WorkingDirectory $root -WindowStyle Hidden
    Start-Sleep -Seconds 4
}

& (Join-Path $root 'Status.ps1')
