[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("Install", "Upgrade")]
    [string]$PackageType = "Upgrade",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [switch]$SkipFrontendBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyy.MM.dd.HHmm"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\packages"
}

$workRoot = Join-Path $repoRoot "artifacts\package-work\$PackageType-$Version"
$publishDir = Join-Path $workRoot "publish"
$packageRoot = Join-Path $workRoot "package"
$payloadDir = Join-Path $packageRoot "payload"
$toolsDir = Join-Path $packageRoot "tools"
$packagePath = Join-Path $OutputDirectory "IPC.Gateway-$PackageType-$Version.zip"

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir, $payloadDir, $toolsDir, $OutputDirectory -Force | Out-Null

if (-not $SkipFrontendBuild) {
    Push-Location (Join-Path $repoRoot "IPC.Gateway.Web")
    try {
        npm run build
    }
    finally {
        Pop-Location
    }
}

$publishArgs = @(
    "publish",
    (Join-Path $repoRoot "IPC.Gateway.WebHost\IPC.Gateway.WebHost.csproj"),
    "-c",
    $Configuration,
    "-o",
    $publishDir,
    "/p:Version=$Version"
)
if ($NoRestore) {
    $publishArgs += "--no-restore"
}
dotnet @publishArgs

$pluginPublishDir = Join-Path $workRoot "drivers\LegacyProtocolPlugins"
$pluginPublishArgs = @(
    "publish",
    (Join-Path $repoRoot "IPC.Gateway.LegacyProtocolPlugins\IPC.Gateway.LegacyProtocolPlugins.csproj"),
    "-c",
    $Configuration,
    "-o",
    $pluginPublishDir,
    "/p:Version=$Version",
    "/p:UseAppHost=false"
)
if ($NoRestore) {
    $pluginPublishArgs += "--no-restore"
}
dotnet @pluginPublishArgs

$driversDir = Join-Path $publishDir "Drivers"
New-Item -ItemType Directory -Path $driversDir -Force | Out-Null
Copy-Item -Path (Join-Path $pluginPublishDir "*") -Destination $driversDir -Recurse -Force

Copy-Item -Path (Join-Path $publishDir "*") -Destination $payloadDir -Recurse -Force

$scriptCandidates = @(
    "deployment\windows\install-ipc-gateway-service.ps1",
    "deployment\windows\remove-ipc-gateway-service.ps1",
    "deployment\windows\apply-ipc-gateway-offline-update.ps1",
    "deployment\linux\install-ipc-gateway-systemd.sh",
    "deployment\linux\ipc-gateway.service"
)
foreach ($relative in $scriptCandidates) {
    $path = Join-Path $repoRoot $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination $toolsDir -Force
    }
}

$manifest = [ordered]@{
    packageId = "ipc-gateway-$PackageType-$Version"
    product = "IPC.Gateway"
    packageType = $PackageType
    version = $Version
    minVersion = ""
    createdTime = (Get-Date).ToUniversalTime().ToString("O")
    entryDirectory = "payload"
    requiresRestart = $true
    description = "$PackageType package for IPC Gateway $Version"
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packageRoot "ipc-gateway-package.json") -Encoding UTF8

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$created = $false
for ($attempt = 1; $attempt -le 3 -and -not $created; $attempt++) {
    try {
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $packageRoot,
            $packagePath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
        $created = $true
    }
    catch {
        if ($attempt -ge 3) {
            throw
        }
        Start-Sleep -Seconds 1
        if (Test-Path -LiteralPath $packagePath) {
            Remove-Item -LiteralPath $packagePath -Force
        }
    }
}

$hash = Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
Write-Host "Package: $packagePath"
Write-Host "SHA256 : $($hash.Hash.ToLowerInvariant())"
