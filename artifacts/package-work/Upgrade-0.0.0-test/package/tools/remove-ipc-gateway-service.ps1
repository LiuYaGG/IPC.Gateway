[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = "IPCGateway",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Windows service '$ServiceName' is not installed."
    return
}

if ($service.Status -ne "Stopped") {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop Windows service")) {
        Stop-Service -Name $ServiceName -Force:$Force
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Delete Windows service")) {
    sc.exe delete $ServiceName | Out-Null
}

Write-Host "Windows service '$ServiceName' has been removed."
