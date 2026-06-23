[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = "IPCGateway",
    [Parameter(Mandatory = $true)]
    [string]$PendingActionPath,
    [switch]$SkipServiceControl
)

$ErrorActionPreference = "Stop"

$resolvedPendingActionPath = (Resolve-Path -LiteralPath $PendingActionPath).Path
$action = Get-Content -LiteralPath $resolvedPendingActionPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($action.SourceDirectory) -or -not (Test-Path -LiteralPath $action.SourceDirectory -PathType Container)) {
    throw "SourceDirectory does not exist: $($action.SourceDirectory)"
}
if ([string]::IsNullOrWhiteSpace($action.TargetDirectory) -or -not (Test-Path -LiteralPath $action.TargetDirectory -PathType Container)) {
    throw "TargetDirectory does not exist: $($action.TargetDirectory)"
}

if (-not $SkipServiceControl) {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        if ($PSCmdlet.ShouldProcess($ServiceName, "Stop service")) {
            Stop-Service -Name $ServiceName -Force
            $service.WaitForStatus("Stopped", "00:00:30")
        }
    }
}

$robocopyArgs = @(
    "`"$($action.SourceDirectory)`"",
    "`"$($action.TargetDirectory)`"",
    "/MIR",
    "/R:2",
    "/W:1",
    "/XD",
    "`"Data`"",
    "/XF",
    "`"appsettings.json`"",
    "`"appsettings.Production.json`"",
    "`"appsettings.Development.json`""
)
$process = Start-Process -FilePath "robocopy.exe" -ArgumentList $robocopyArgs -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ge 8) {
    throw "Robocopy failed. Exit code: $($process.ExitCode)"
}

$action.Status = "Applied"
$action.AppliedTime = (Get-Date).ToUniversalTime().ToString("O")
$action | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resolvedPendingActionPath -Encoding UTF8

if (-not $SkipServiceControl) {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        if ($PSCmdlet.ShouldProcess($ServiceName, "Start service")) {
            Start-Service -Name $ServiceName
        }
    }
}

Write-Host "IPC Gateway offline $($action.ActionType) completed."
