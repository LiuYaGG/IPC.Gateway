$ErrorActionPreference = 'Stop'
$root = 'D:\IPC-Simulators'
$taskName = 'IPC Protocol Simulators'
$python = Join-Path $root 'Python\python.exe'
$hostScript = Join-Path $root 'simulator_host.py'

$action = New-ScheduledTaskAction -Execute $python -Argument ('"' + $hostScript + '"') -WorkingDirectory $root
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Free local industrial protocol simulators for IPC Gateway testing (current user)' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Installed and started scheduled task: $taskName"
