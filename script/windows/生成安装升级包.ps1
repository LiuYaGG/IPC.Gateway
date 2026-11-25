[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("Install", "Upgrade")]
    [string]$PackageType = "Upgrade",
    [string]$Configuration = "Release",
    [string]$RepositoryRoot = "",
    [ValidateSet('', 'win-x64', 'linux-x64', 'linux-arm64')]
    [string]$RuntimeIdentifier = "",
    [string]$UosSqlitePackage = "",
    [string]$OutputDirectory = "",
    [string]$BuildId = "",
    [string]$SigningPrivateKeyPath = "",
    [string]$Signer = "",
    [switch]$SkipFrontendBuild,
    [switch]$LegacyUpgradeManifest,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) { $RuntimeIdentifier = 'win-x64' }

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

function New-GatewayPackageZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    # Windows PowerShell 5.1 uses .NET Framework, whose CreateFromDirectory
    # can retain Windows separators. ZIP entry paths must always use '/'.
    # Explicit UTF-8 also sets the Unicode filename flag for Linux readers.
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.FileStream]::new($DestinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        # Use the shared UTF8 instance: .NET Framework only sets the EFS flag
        # reliably for Encoding.UTF8, not a separate UTF8Encoding instance.
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true, [Text.Encoding]::UTF8)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName) {
                $entryName = Get-RelativeZipPath -Root $SourceDirectory -Path $file.FullName
                if ([string]::IsNullOrWhiteSpace($entryName) -or $entryName.Contains('\') -or $entryName.StartsWith('/') -or
                    $entryName.Split('/') -contains '..') { throw "Invalid package entry path: $entryName" }
                $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                if ($file.LastWriteTime.Year -ge 1980 -and $file.LastWriteTime.Year -le 2107) {
                    $entry.LastWriteTime = [DateTimeOffset]$file.LastWriteTime
                }
                $inputStream = $file.OpenRead()
                try {
                    $outputStream = $entry.Open()
                    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
                }
                finally { $inputStream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
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
    if ($Manifest.manifestVersion -ge 3) {
        foreach ($field in 'operatingSystem','architecture','runtimeIdentifier','targetFramework','requiredDotNetMajor','minimumHaStateVersion','maximumHaStateVersion','minimumGlibcVersion') {
            Add-SigningLine -Builder $builder -Key $field -Value $Manifest.compatibility.$field
        }
    }

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
if ($UosSqlitePackage) {
    if ($RuntimeIdentifier -ne 'linux-x64') { throw 'UOS glibc 2.28 requires -RuntimeIdentifier linux-x64.' }
    $UosSqlitePackage = (Resolve-Path -LiteralPath $UosSqlitePackage).Path
    $platformSuffix += '-uos-glibc2.28'
}
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
foreach ($page in 'index.html','ha-setup.html') {
    if (!(Test-Path -LiteralPath (Join-Path $repoRoot "src/IPC.Gateway.Web/dist/$page") -PathType Leaf)) {
        throw "Missing frontend entry $page. Rebuild the frontend before packaging."
    }
}
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

# The voter and the setup wizard ship alongside the gateway. An online gateway
# update must never overwrite a DLL that a live voter is using.
foreach ($component in @('IPC.Gateway.Arbiter','IPC.Gateway.HaSetup','IPC.Gateway.UpdateWorker')) {
    $componentArgs = @('publish',(Join-Path $repoRoot "src/$component/$component.csproj"),'-c',$Configuration,
        '-o',(Join-Path $packageRoot "components/$component"),"/p:Version=$Version",'/p:UseAppHost=false','--self-contained','false')
    if ($RuntimeIdentifier) { $componentArgs += @('-r',$RuntimeIdentifier) }
    if ($NoRestore) { $componentArgs += '--no-restore' }
    Invoke-BuildTool -FilePath $dotnetPath -ArgumentList $componentArgs -FailureMessage "$component publish failed."
}

foreach ($file in Get-ChildItem -LiteralPath $publishDir -Recurse -File) {
    $relative = Get-RelativeZipPath -Root $publishDir -Path $file.FullName
    if ($relative -like 'Data/*' -or $file.Name -in @('appsettings.Production.json', 'appsettings.Development.json')) { continue }
    $destination = Join-Path $payloadDir $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}

if ($UosSqlitePackage) {
    # Accept only the SQLite binaries verified on UOS and the original dependencies they replace.
    # A NuGet upgrade requires revalidating the UOS build before changing these digests.
    $sqliteFiles = @(
        @{ Path='libe_sqlite3.so'; Original='b0f5c48026fedcf9f05cf31c6fadf9e1e001ca59cd85aa68b8b6814e52c18a07'; Compat='097fc23211bb8a99cdbdb97c3b9679187fc8f28bb23557bf0a4d1bf835000ed4' }
        @{ Path='Drivers/libe_sqlite3.so'; Original='a5e8aed525023e6c209d37d3e762e2e76ed3a830464569a419abfc3cf12d7a6c'; Compat='967020205508018ec89081fc36cfe3384acd500deedcfb9be17db3b42beb4bbd' }
        @{ Path='Drivers/runtimes/linux-x64/native/libe_sqlite3.so'; Original='a5e8aed525023e6c209d37d3e762e2e76ed3a830464569a419abfc3cf12d7a6c'; Compat='967020205508018ec89081fc36cfe3384acd500deedcfb9be17db3b42beb4bbd' }
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($UosSqlitePackage)
    try {
        foreach ($library in $sqliteFiles) {
            $destination = Join-Path $payloadDir $library.Path
            if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash -ne $library.Original) {
                throw "Unexpected SQLite dependency for $($library.Path). Validate its UOS compatibility before packaging."
            }
            $entry = $archive.GetEntry('payload/' + $library.Path)
            if (!$entry -or $entry.Length -gt 4MB) { throw "Invalid UOS SQLite entry: $($library.Path)" }
            $stream = $entry.Open()
            try { $digest = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
            if ($digest -ne $library.Compat) { throw "UOS compatibility digest mismatch: $($library.Path)" }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
        }
    }
    finally { $archive.Dispose() }
}

$minimumGlibc = ''
if ($RuntimeIdentifier.StartsWith('linux-')) {
    $minimum = [Version]'2.28'
    $machine = if ($RuntimeIdentifier -eq 'linux-arm64') { 183 } else { 62 }
    foreach ($native in Get-ChildItem -LiteralPath $payloadDir -Recurse -File) {
        if ($native.Name -notmatch '\.so($|\.)') { continue }
        $bytes = [IO.File]::ReadAllBytes($native.FullName)
        if ($bytes.Length -lt 20 -or $bytes[0] -ne 127 -or $bytes[1] -ne 69 -or $bytes[2] -ne 76 -or $bytes[3] -ne 70) { continue }
        # Include only the selected architecture. Opposite-architecture runtime assets do not load.
        if ([BitConverter]::ToUInt16($bytes,18) -ne $machine) { continue }
        $binary = [Text.Encoding]::ASCII.GetString($bytes)
        foreach ($match in [regex]::Matches($binary,'GLIBC_([0-9]+\.[0-9]+(?:\.[0-9]+)?)')) {
            $required = [Version]$match.Groups[1].Value
            if ($required -gt $minimum) { $minimum = $required }
        }
    }
    $minimumGlibc = $minimum.ToString()
}
$compatibility = [ordered]@{
    operatingSystem = if ($RuntimeIdentifier.StartsWith('win-')) { 'windows' } else { 'linux' }
    architecture = $RuntimeIdentifier.Split('-')[-1]
    runtimeIdentifier = $RuntimeIdentifier
    targetFramework = 'net10.0'
    requiredDotNetMajor = 10
    minimumHaStateVersion = 2
    maximumHaStateVersion = 3
    minimumGlibcVersion = $minimumGlibc
}
[IO.File]::WriteAllText((Join-Path $payloadDir 'ipc-gateway-runtime.json'), ($compatibility | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

$scriptCandidates = @(
    "script\windows\安装Windows服务.ps1",
    "script\windows\发布仲裁客户端.ps1",
    "script\windows\安装热备服务.ps1",
    "script\windows\打开热备配置.ps1",
    "script\windows\打开热备配置.cmd",
    "script\windows\卸载Windows服务.ps1",
    "script\windows\执行离线升级.ps1",
    "script\linux\发布Linux服务.sh",
    "script\linux\发布仲裁客户端.sh",
    "script\linux\安装热备服务.sh",
    "script\linux\打开热备配置.sh",
    "script\linux\卸载Linux服务.sh",
    "script\linux\解压安装升级包.py",
    "script\linux\gateway.env.example",
    "docs\发布部署指南.md",
    "docs\双机热备卸载与重新发布.md",
    "script\linux\ipc-gateway.service"
)
foreach ($relative in $scriptCandidates) {
    $path = Join-Path $repoRoot $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package tool: $relative" }
    Copy-Item -LiteralPath $path -Destination $toolsDir -Force
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
    manifestVersion = if ($LegacyUpgradeManifest) { 2 } else { 3 }
    packageId = "ipc-gateway-$PackageType-$Version$platformSuffix"
    product = "IPC.Gateway"
    packageType = $PackageType
    version = $Version
    minVersion = ""
    compatibility = $compatibility
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
        New-GatewayPackageZip -SourceDirectory $packageRoot -DestinationPath $packagePath
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
