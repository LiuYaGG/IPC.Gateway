$ErrorActionPreference = 'Stop'
$group = 'IPC Protocol Simulators'

Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule

New-NetFirewallRule -DisplayName 'IPC Simulators TCP' -Group $group -Direction Inbound -Action Allow -Protocol TCP -LocalPort 1102,1502,20000,44818 -Profile Private -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -DisplayName 'IPC Simulators UDP' -Group $group -Direction Inbound -Action Allow -Protocol UDP -LocalPort 1161,47808,44818 -Profile Private -RemoteAddress LocalSubnet | Out-Null

Write-Host 'Private-profile firewall rules installed for LocalSubnet only.'
