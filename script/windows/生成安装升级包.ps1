[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("Install", "Upgrade")]
    [string]$PackageType = "Upgrade",
    [string]$Configuration = "Release",
    [string]$RepositoryRoot = "",
    [ValidateSet('', 'win-x64', 'linux-x64', 'linux-arm64')]
    [string]$RuntimeIdentifier = "",
    [string]$OutputDirectory = "",
    [string]$BuildId = "",
    [string]$SigningPrivateKeyPath = "",
    [string]$Signer = "",
    [switch]$SkipFrontendBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

function Invoke-BuildTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    # Windows PowerShell ISE turns native stderr (including Vite warnings) into
    # PowerShell errors. Keep the output visible and use the process exit code
    # to decide whether the build failed, then restore strict script errors.
    $previousErrorActionPreference = $ErrorActionPreference
    $exitCode = $null
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        & $FilePath @ArgumentList 2>&1 | ForEach-Object {
            $line = if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception.Message } else { $_.ToString() }
            Write-Host ([regex]::Replace($line, '\x1B\[[0-?]*[ -/]*[@-~]', ''))
        }
        $exitCode = $global:LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($null -eq $exitCode -or $exitCode -ne 0) {
        throw "$FailureMessage Exit code: $exitCode"
    }
}

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

    # Match the server's ordinal ordering, including Chinese tool filenames.
    $orderedFiles = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Manifest.files) { $orderedFiles.Add([string]$file.path, $file) }
    foreach ($file in $orderedFiles.Values) {
        Add-SigningLine -Builder $builder -Key "file" -Value "$($file.path)|$($file.sizeBytes)|$($file.sha256)"
    }

    return $builder.ToString()
}

function Resolve-PackageRepositoryRoot {
    param([string]$Root, [string]$ScriptDirectory)
    if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path $ScriptDirectory '../..' }
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedRoot 'src/IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj') -PathType Leaf)) {
        throw 'Source project not found. If this script was moved separately, specify -RepositoryRoot with the source repository directory.'
    }
    return $resolvedRoot
}
$repoRoot = Resolve-PackageRepositoryRoot -Root $RepositoryRoot -ScriptDirectory $PSScriptRoot
# Resolve applications explicitly so ISE does not pick npm.ps1 instead of npm.cmd.
$isWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$dotnetCommand = if ($isWindowsHost) { 'dotnet.exe' } else { 'dotnet' }
$npmCommand = if ($isWindowsHost) { 'npm.cmd' } else { 'npm' }
$dotnetPath = (Get-Command $dotnetCommand -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if (-not $SkipFrontendBuild) {
    $npmPath = (Get-Command $npmCommand -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyy.MM.dd.HHmm"
}
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]*$') { throw 'Version must be a safe version string without path separators.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\packages"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = "local-" + (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
}

$platformSuffix = if ($RuntimeIdentifier) { "-$RuntimeIdentifier" } else { '' }
$workRoot = Join-Path $repoRoot "artifacts/package-work/$PackageType-$Version$platformSuffix"
$workRoot = [IO.Path]::GetFullPath($workRoot)
$allowedWorkRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/package-work')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $workRoot.StartsWith($allowedWorkRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package working directory is outside the expected workspace.' }
$publishDir = Join-Path $workRoot "publish"
$packageRoot = Join-Path $workRoot "package"
$payloadDir = Join-Path $packageRoot "payload"
$toolsDir = Join-Path $packageRoot "tools"
$packagePath = Join-Path $OutputDirectory "IPC.Gateway-$PackageType-$Version$platformSuffix.zip"

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir, $payloadDir, $toolsDir, $OutputDirectory -Force | Out-Null

if (-not $SkipFrontendBuild) {
    Push-Location (Join-Path $repoRoot "src/IPC.Gateway.Web")
    try {
        Invoke-BuildTool -FilePath $npmPath -ArgumentList @('run', 'build') -FailureMessage 'Frontend build failed.'
    }
    finally {
        Pop-Location
    }
}

$publishArgs = @(
    "publish",
    (Join-Path $repoRoot "src/IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj"),
    "-c",
    $Configuration,
    "-o",
    $publishDir,
    "/p:Version=$Version",
    "/p:UseAppHost=false",
    "--self-contained", "false"
)
if ($RuntimeIdentifier) { $publishArgs += @('--runtime', $RuntimeIdentifier) }
if ($NoRestore) {
    $publishArgs += "--no-restore"
}
Invoke-BuildTool -FilePath $dotnetPath -ArgumentList $publishArgs -FailureMessage 'WebHost publish failed.'

$pluginPublishDir = Join-Path $workRoot "drivers\LegacyProtocolPlugins"
$pluginPublishArgs = @(
    "publish",
    (Join-Path $repoRoot "src/IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj"),
    "-c",
    $Configuration,
    "-o",
    $pluginPublishDir,
    "/p:Version=$Version",
    "/p:UseAppHost=false"
)
if ($RuntimeIdentifier) { $pluginPublishArgs += @('--runtime', $RuntimeIdentifier, '--self-contained', 'false') }
if ($NoRestore) {
    $pluginPublishArgs += "--no-restore"
}
Invoke-BuildTool -FilePath $dotnetPath -ArgumentList $pluginPublishArgs -FailureMessage 'Protocol driver publish failed.'

$driversDir = Join-Path $publishDir "Drivers"
New-Item -ItemType Directory -Path $driversDir -Force | Out-Null
Copy-Item -Path (Join-Path $pluginPublishDir "*") -Destination $driversDir -Recurse -Force

foreach ($file in Get-ChildItem -LiteralPath $publishDir -Recurse -File) {
    $relative = Get-RelativeZipPath -Root $publishDir -Path $file.FullName
    if ($relative -like 'Data/*' -or $file.Name -in @('appsettings.Production.json', 'appsettings.Development.json')) { continue }
    $destination = Join-Path $payloadDir $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}

$scriptCandidates = @(
    "script\windows\安装Windows服务.ps1",
    "script\windows\卸载Windows服务.ps1",
    "script\windows\执行离线升级.ps1",
    "script\linux\发布Linux服务.sh",
    "script\linux\卸载Linux服务.sh",
    "script\linux\gateway.env.example",
    "docs\发布部署指南.md",
    "script\linux\ipc-gateway.service"
)
foreach ($relative in $scriptCandidates) {
    $path = Join-Path $repoRoot $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination $toolsDir -Force
    }
}
# Keep the Docker directory layout; building an image still requires the source
# checkout, or use -NoBuild / NO_BUILD=1 with an already loaded release image.
$dockerToolsDir = Join-Path $toolsDir 'docker'
New-Item -ItemType Directory -Path $dockerToolsDir -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'script/docker') -File |
    Where-Object { $_.Extension -in @('.sh', '.ps1', '.yml', '.example', '.md') } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dockerToolsDir -Force }

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
    packageId = "ipc-gateway-$PackageType-$Version$platformSuffix"
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
