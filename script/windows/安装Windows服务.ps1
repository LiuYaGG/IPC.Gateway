[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ServiceName = "IPCGateway",
    [string]$DisplayName = "IPC Gateway Edge Service",
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$Url = "http://127.0.0.1:5184",
    [string]$DataDirectory = "",
    [string[]]$Environment = @(),
    [switch]$EnableServiceRecovery,
    [int]$RestartDelaySeconds = 30,
    [int]$ResetFailureCountDays = 1,
    [switch]$Start
)

$ErrorActionPreference = "Stop"

if (-not $WhatIfPreference) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '请以管理员身份运行 PowerShell，再安装或更新 Windows 服务。'
    }
}

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$dllPath = Join-Path $resolvedPublishDirectory "IPC.Gateway.WebHost.dll"
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "IPC.Gateway.WebHost.dll was not found in '$resolvedPublishDirectory'. Publish IPC.Gateway.WebHost before installing the service."
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$binaryPath = "`"$dotnet`" `"$dllPath`" --contentRoot `"$resolvedPublishDirectory`""
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existingService) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Update Windows service command line")) {
        $serviceConfig = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
        $changeResult = Invoke-CimMethod -InputObject $serviceConfig -MethodName Change -Arguments @{
            PathName = $binaryPath
            StartMode = 'Automatic'
            DisplayName = $DisplayName
        }
        if ($changeResult.ReturnValue -ne 0) {
            throw "Updating service '$ServiceName' failed with code $($changeResult.ReturnValue)."
        }
    }
}
else {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Create Windows service")) {
        New-Service -Name $ServiceName -DisplayName $DisplayName -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
    }
}

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$serviceEnvironment = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "DOTNET_ENVIRONMENT=Production",
    "ASPNETCORE_URLS=$Url"
)
if ($EnableServiceRecovery) {
    $serviceEnvironment += "Gateway__Watchdog__RequestHostStopOnUnrecoverable=true"
}
if (-not [string]::IsNullOrWhiteSpace($DataDirectory)) {
    $resolvedDataDirectory = $DataDirectory
    if (-not [System.IO.Path]::IsPathRooted($resolvedDataDirectory)) {
        $resolvedDataDirectory = Join-Path $resolvedPublishDirectory $resolvedDataDirectory
    }
    $resolvedDataDirectory = [IO.Path]::GetFullPath($resolvedDataDirectory)
    if ($PSCmdlet.ShouldProcess($resolvedDataDirectory, 'Create data directory')) {
        New-Item -ItemType Directory -Path $resolvedDataDirectory -Force | Out-Null
    }
    $serviceEnvironment += "Gateway__History__Directory=$resolvedDataDirectory\History"
    $serviceEnvironment += "Gateway__Mqtt__OutboxDirectory=$resolvedDataDirectory\MqttOutbox"
    $serviceEnvironment += "Gateway__Maintenance__Updates__UpdateDirectory=$resolvedDataDirectory\Updates"
    $serviceEnvironment += "Gateway__OpcUa__CertificateStorePath=$resolvedDataDirectory\OpcUa\pki"
    $serviceEnvironment += "Gateway__Watchdog__StateDirectory=$resolvedDataDirectory\Watchdog"
    $serviceEnvironment += "Gateway__Inference__ModelDirectory=$resolvedDataDirectory\Models"
    $serviceEnvironment += "Gateway__Scripting__ConfigurationFile=$resolvedDataDirectory\Scripting\config.json"
}
foreach ($entry in $Environment) {
    if (-not [string]::IsNullOrWhiteSpace($entry)) {
        $serviceEnvironment += $entry
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Set service environment variables")) {
    if (-not (Test-Path -LiteralPath $serviceKey)) {
        throw "Service registry key '$serviceKey' was not found."
    }

    $environmentMap = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    $existingEnvironment = (Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue).Environment
    foreach ($entry in @($existingEnvironment) + $serviceEnvironment) {
        if ($entry -match '^([^=]+)=(.*)$') { $environmentMap[$Matches[1]] = $Matches[2] }
    }
    $mergedEnvironment = @($environmentMap.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
    if ($null -ne $existingEnvironment) {
        Set-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $mergedEnvironment
    }
    else {
        New-ItemProperty -LiteralPath $serviceKey -Name Environment -PropertyType MultiString -Value $mergedEnvironment | Out-Null
    }
}

if ($EnableServiceRecovery) {
    $delayMs = [Math]::Max(1, $RestartDelaySeconds) * 1000
    $resetSeconds = [Math]::Max(1, $ResetFailureCountDays) * 86400
    if ($PSCmdlet.ShouldProcess($ServiceName, "Configure service recovery")) {
        sc.exe failure $ServiceName reset= $resetSeconds actions= restart/$delayMs/restart/$delayMs/none/$delayMs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Configuring service recovery failed: $LASTEXITCODE" }
        sc.exe failureflag $ServiceName 1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Configuring service recovery flag failed: $LASTEXITCODE" }
    }
}

if ($Start) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Start Windows service")) {
        Start-Service -Name $ServiceName
    }
}

Write-Host "Windows service '$ServiceName' is configured for $dllPath"
if ($EnableServiceRecovery) {
    Write-Host "Service recovery is enabled. Restart delay: $RestartDelaySeconds seconds. Reset failure count: $ResetFailureCountDays day(s)."
}
