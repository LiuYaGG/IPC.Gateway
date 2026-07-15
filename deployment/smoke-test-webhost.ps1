param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [string]$Url = "",

    [int]$TimeoutSeconds = 60,

    [string]$BootstrapAdminUsername = "admin",

    [string]$BootstrapAdminPassword = "",

    [string]$AuthSecret = "",

    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

function New-FreeLoopbackUrl {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }

    return "http://127.0.0.1:$port"
}

function New-SmokeSecret {
    $bytes = [byte[]]::new(32)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function Invoke-HttpJson {
    param(
        [string]$Uri,
        [int[]]$AllowedStatusCodes = @(200)
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 10
    }
    catch {
        $response = $_.Exception.Response
    }

    if ($null -eq $response) {
        throw "No HTTP response from $Uri."
    }

    $statusCode = [int]$response.StatusCode
    if ($AllowedStatusCodes -notcontains $statusCode) {
        throw "Unexpected HTTP $statusCode from $Uri."
    }

    return $response
}

function Wait-ForEndpoint {
    param(
        [string]$Uri,
        [int[]]$AllowedStatusCodes = @(200),
        [int]$TimeoutSeconds = 60
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            return Invoke-HttpJson -Uri $Uri -AllowedStatusCodes $AllowedStatusCodes
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $Uri. Last error: $lastError"
}

$publishPath = Resolve-Path -LiteralPath $PublishDirectory
$webHostExe = Join-Path $publishPath "IPC.Gateway.WebHost.exe"
if (-not (Test-Path -LiteralPath $webHostExe)) {
    throw "Published WebHost executable was not found: $webHostExe"
}

$indexPath = Join-Path $publishPath "wwwroot\index.html"
if (-not (Test-Path -LiteralPath $indexPath)) {
    throw "Published frontend index was not found: $indexPath"
}

$assetsPath = Join-Path $publishPath "wwwroot\assets"
$assetCount = 0
if (Test-Path -LiteralPath $assetsPath) {
    $assetCount = (Get-ChildItem -LiteralPath $assetsPath -File | Measure-Object).Count
}

if ($assetCount -lt 1) {
    throw "Published frontend assets were not found under: $assetsPath"
}

$developmentConfig = Join-Path $publishPath "appsettings.Development.json"
if (Test-Path -LiteralPath $developmentConfig) {
    throw "Release package contains development configuration: $developmentConfig"
}

if ([string]::IsNullOrWhiteSpace($Url)) {
    $Url = New-FreeLoopbackUrl
}

if ([string]::IsNullOrWhiteSpace($BootstrapAdminPassword)) {
    $BootstrapAdminPassword = New-SmokeSecret
}

if ([string]::IsNullOrWhiteSpace($AuthSecret)) {
    $AuthSecret = New-SmokeSecret
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ipc-gateway-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
$stdoutPath = Join-Path $smokeRoot "webhost.out.log"
$stderrPath = Join-Path $smokeRoot "webhost.err.log"
$databasePath = Join-Path $smokeRoot "gateway-smoke.db"
$historyPath = Join-Path $smokeRoot "history"
$outboxPath = Join-Path $smokeRoot "mqtt-outbox"

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $webHostExe
$startInfo.WorkingDirectory = $publishPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $false
$startInfo.RedirectStandardError = $false
$startInfo.RedirectStandardInput = $false
$environmentOverrides = [ordered]@{
    "ASPNETCORE_ENVIRONMENT" = "Production"
    "DOTNET_ENVIRONMENT" = "Production"
    "ASPNETCORE_URLS" = $Url
    "Gateway__Database__Provider" = "Sqlite"
    "Gateway__Database__Database" = $databasePath
    "Gateway__Database__ConnectionString" = "Data Source=$databasePath;Pooling=False"
    "Gateway__Database__AutoCreateDatabase" = "true"
    "Gateway__Auth__Secret" = $AuthSecret
    "Gateway__Auth__BootstrapAdminUsername" = $BootstrapAdminUsername
    "Gateway__Auth__BootstrapAdminPassword" = $BootstrapAdminPassword
    "Gateway__History__Directory" = $historyPath
    "Gateway__Mqtt__OutboxDirectory" = $outboxPath
    "Gateway__StorageHealth__DegradedAvailableMegabytes" = "1"
    "Gateway__StorageHealth__UnhealthyAvailableMegabytes" = "1"
}
$originalProcessEnvironment = @{}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$process.EnableRaisingEvents = $true

try {
    foreach ($entry in $environmentOverrides.GetEnumerator()) {
        $originalProcessEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable(
            $entry.Key,
            [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            [string]$entry.Value,
            [EnvironmentVariableTarget]::Process)
    }

    if (-not $process.Start()) {
        throw "Failed to start published WebHost."
    }

    Wait-ForEndpoint -Uri "$Url/health/live" -AllowedStatusCodes @(200) -TimeoutSeconds $TimeoutSeconds | Out-Null
    $readyResponse = Wait-ForEndpoint -Uri "$Url/api/health/ready" -AllowedStatusCodes @(200, 503) -TimeoutSeconds $TimeoutSeconds
    $rootResponse = Invoke-HttpJson -Uri "$Url/" -AllowedStatusCodes @(200)

    $readyJson = $readyResponse.Content | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($readyJson.status)) {
        throw "Readiness response did not include a status field."
    }

    if ($rootResponse.Content -notmatch "<!doctype html" -and $rootResponse.Content -notmatch "<html") {
        throw "Root endpoint did not return HTML."
    }

    [pscustomobject]@{
        PublishDirectory = $publishPath.Path
        Url = $Url
        ProcessId = $process.Id
        AdminUsername = $BootstrapAdminUsername
        ReadyStatus = $readyJson.status
        ReadyHttpStatus = [int]$readyResponse.StatusCode
        FrontendAssetCount = $assetCount
        DevelopmentConfigPresent = $false
        SmokeRoot = $smokeRoot
    } | ConvertTo-Json
}
finally {
    if (-not $KeepRunning -and $process -ne $null -and -not $process.HasExited) {
        try {
            $process.Kill()
            $process.WaitForExit(10000) | Out-Null
        }
        catch {
        }
    }

    foreach ($entry in $originalProcessEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process)
    }
}
