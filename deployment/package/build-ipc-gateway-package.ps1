[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("Install", "Upgrade")]
    [string]$PackageType = "Upgrade",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string]$BuildId = "",
    [string]$SigningPrivateKeyPath = "",
    [string]$Signer = "",
    [switch]$SkipFrontendBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

function Get-RelativeZipPath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $filePath = [System.IO.Path]::GetFullPath($Path)
    $relative = [Uri]::UnescapeDataString(([Uri]$rootPath).MakeRelativeUri([Uri]$filePath).ToString())
    return $relative.Replace("\", "/")
}

function Add-SigningLine {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Key,
        [object]$Value
    )

    $text = ""
    if ($null -ne $Value) {
        $text = [string]$Value
    }
    $text = $text.Replace("`r", " ").Replace("`n", " ")
    [void]$Builder.Append($Key).Append("=").Append($text).Append("`n")
}

function Get-PackageSigningPayload {
    param(
        [object]$Manifest
    )

    $builder = [System.Text.StringBuilder]::new()
    Add-SigningLine -Builder $builder -Key "manifestVersion" -Value $Manifest.manifestVersion
    Add-SigningLine -Builder $builder -Key "packageId" -Value $Manifest.packageId
    Add-SigningLine -Builder $builder -Key "product" -Value $Manifest.product
    Add-SigningLine -Builder $builder -Key "packageType" -Value $Manifest.packageType
    Add-SigningLine -Builder $builder -Key "version" -Value $Manifest.version
    Add-SigningLine -Builder $builder -Key "minVersion" -Value $Manifest.minVersion
    Add-SigningLine -Builder $builder -Key "buildId" -Value $Manifest.buildId
    Add-SigningLine -Builder $builder -Key "entryDirectory" -Value $Manifest.entryDirectory
    Add-SigningLine -Builder $builder -Key "requiresRestart" -Value ([string]$Manifest.requiresRestart).ToLowerInvariant()
    Add-SigningLine -Builder $builder -Key "hashAlgorithm" -Value $Manifest.hashAlgorithm
    Add-SigningLine -Builder $builder -Key "signatureAlgorithm" -Value $Manifest.signatureAlgorithm
    Add-SigningLine -Builder $builder -Key "signer" -Value $Manifest.signer
    Add-SigningLine -Builder $builder -Key "signedTime" -Value $Manifest.signedTime

    foreach ($file in ($Manifest.files | Sort-Object -Property path)) {
        Add-SigningLine -Builder $builder -Key "file" -Value "$($file.path)|$($file.sizeBytes)|$($file.sha256)"
    }

    return $builder.ToString()
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyy.MM.dd.HHmm"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\packages"
}
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = "local-" + (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
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

$packageFiles = @(
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Sort-Object -Property FullName |
        ForEach-Object {
            $digest = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            [ordered]@{
                path = Get-RelativeZipPath -Root $packageRoot -Path $_.FullName
                sha256 = $digest.Hash.ToLowerInvariant()
                sizeBytes = $_.Length
            }
        }
)

$manifest = [ordered]@{
    manifestVersion = 2
    packageId = "ipc-gateway-$PackageType-$Version"
    product = "IPC.Gateway"
    packageType = $PackageType
    version = $Version
    minVersion = ""
    createdTime = (Get-Date).ToUniversalTime().ToString("O")
    buildId = $BuildId
    entryDirectory = "payload"
    requiresRestart = $true
    description = "$PackageType package for IPC Gateway $Version"
    hashAlgorithm = "SHA256"
    files = $packageFiles
    signatureAlgorithm = ""
    signature = ""
    signer = ""
    signedTime = $null
}

if (-not [string]::IsNullOrWhiteSpace($SigningPrivateKeyPath)) {
    $resolvedKeyPath = (Resolve-Path -LiteralPath $SigningPrivateKeyPath).Path
    $manifest.signatureAlgorithm = "RS256"
    $manifest.signer = if ([string]::IsNullOrWhiteSpace($Signer)) { [Environment]::UserName } else { $Signer }
    $manifest.signedTime = (Get-Date).ToUniversalTime().ToString("O")

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $pem = [System.IO.File]::ReadAllText($resolvedKeyPath)
        $rsa.ImportFromPem($pem)
        $payload = Get-PackageSigningPayload -Manifest $manifest
        $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        $signatureBytes = $rsa.SignData(
            $payloadBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $manifest.signature = [Convert]::ToBase64String($signatureBytes)
    }
    catch [System.Management.Automation.MethodException] {
        throw "Package signing requires a PowerShell runtime that supports RSA.ImportFromPem. Use PowerShell 7+ or sign in CI."
    }
    finally {
        $rsa.Dispose()
    }
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
