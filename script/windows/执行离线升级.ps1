[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = "IPCGateway",
    [Parameter(Mandatory = $true)]
    [string]$PendingActionPath,
    [switch]$SkipServiceControl,
    [ValidateRange(1, 86400)][int]$ServiceTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

$resolvedPendingActionPath = (Resolve-Path -LiteralPath $PendingActionPath).Path
$lock = [IO.File]::Open($resolvedPendingActionPath + '.lock', 'OpenOrCreate', 'ReadWrite', 'None')
$executing = $false
$oldHostExited = $false
function Save-ActionReceipt {
    $temporary = $resolvedPendingActionPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($action | ConvertTo-Json -Depth 20))
        for ($attempt = 0; ; $attempt++) {
            try {
                [IO.File]::Replace($temporary, $resolvedPendingActionPath, [System.Management.Automation.Language.NullString]::Value)
                return
            } catch [IO.IOException] {
                if ($attempt -ge 9) { throw }
                Start-Sleep -Milliseconds 50
            }
        }
    } finally { [IO.File]::Delete($temporary) }
}
try {
$action = Get-Content -LiteralPath $resolvedPendingActionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($action.Status -ne 'Pending') { throw 'Only a pending action can be started manually.' }
if (-not $PSCmdlet.ShouldProcess($action.TargetDirectory, "Apply $($action.ActionType)")) { return }
$executing = $true
$sourceRoot = [IO.Path]::GetFullPath($action.SourceDirectory).TrimEnd('\')
$targetRoot = [IO.Path]::GetFullPath($action.TargetDirectory).TrimEnd('\')
if ($sourceRoot -eq $targetRoot -or $targetRoot -eq [IO.Path]::GetPathRoot($targetRoot).TrimEnd('\')) {
    throw 'Unsafe source or target directory.'
}

if ([string]::IsNullOrWhiteSpace($action.SourceDirectory) -or -not (Test-Path -LiteralPath $action.SourceDirectory -PathType Container)) {
    throw "SourceDirectory does not exist: $($action.SourceDirectory)"
}
if ([string]::IsNullOrWhiteSpace($action.TargetDirectory) -or -not (Test-Path -LiteralPath $action.TargetDirectory -PathType Container)) {
    throw "TargetDirectory does not exist: $($action.TargetDirectory)"
}

$action.Status = 'Applying'
Save-ActionReceipt
if (-not $SkipServiceControl) {
    try { $service = Get-Service -Name $ServiceName -ErrorAction Stop }
    catch {
        if ($_.FullyQualifiedErrorId -notlike 'NoServiceFoundForGivenName,*') { throw }
        $SkipServiceControl = $true
    }
}
if (-not $SkipServiceControl) {
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    $oldProcess = $null
    try {
        if ($serviceInfo.ProcessId -gt 0) {
            try { $oldProcess = [Diagnostics.Process]::GetProcessById([int]$serviceInfo.ProcessId); $null = $oldProcess.Handle }
            catch [ArgumentException] { $oldProcess = $null }
        } elseif ($service.Status -ne 'Stopped') { throw 'Cannot identify the running service process.' }
        $service.Refresh()
        if ($service.Status -notin @('Stopped', 'StopPending')) { $service.Stop() }
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
        if ($oldProcess -and -not $oldProcess.WaitForExit($ServiceTimeoutSeconds * 1000)) { throw 'The old host process has not exited. No files were copied.' }
        $oldHostExited = $true
    } finally { if ($oldProcess) { $oldProcess.Dispose() } }
}

$robocopyArgs = @(
    "`"$sourceRoot`"",
    "`"$targetRoot`"",
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

if (-not $SkipServiceControl) {
    $service.Start()
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
}
$action.Status = "Applied"
$action | Add-Member -MemberType NoteProperty -Name AppliedTime -Value ((Get-Date).ToUniversalTime().ToString("O")) -Force
$action | Add-Member -MemberType NoteProperty -Name ErrorMessage -Value '' -Force
Save-ActionReceipt
Write-Host "IPC Gateway offline $($action.ActionType) completed."
} catch {
    if ($executing) {
        $action.Status = 'Failed'
        $action | Add-Member -MemberType NoteProperty -Name ErrorMessage -Value $_.Exception.Message -Force
        Save-ActionReceipt
    }
    if ($oldHostExited) {
        try { $service.Refresh(); if ($service.Status -eq 'Stopped') { $service.Start() } } catch { Write-Warning $_.Exception.Message }
    }
    throw
} finally { $lock.Dispose() }
