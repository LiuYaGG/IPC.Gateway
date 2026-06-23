[CmdletBinding(SupportsShouldProcess = $true)]
param(
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

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$dllPath = Join-Path $resolvedPublishDirectory "IPC.Gateway.WebHost.dll"
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "IPC.Gateway.WebHost.dll was not found in '$resolvedPublishDirectory'. Publish IPC.Gateway.WebHost before installing the service."
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$binaryPath = "`"$dotnet`" `"$dllPath`""
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existingService) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Update Windows service command line")) {
        sc.exe config $ServiceName binPath= $binaryPath start= auto DisplayName= $DisplayName | Out-Null
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
    New-Item -ItemType Directory -Path $resolvedDataDirectory -Force | Out-Null
    $serviceEnvironment += "Gateway__History__Directory=$resolvedDataDirectory\History"
    $serviceEnvironment += "Gateway__Mqtt__OutboxDirectory=$resolvedDataDirectory\MqttOutbox"
    $serviceEnvironment += "Gateway__Maintenance__Updates__UpdateDirectory=$resolvedDataDirectory\Updates"
    $serviceEnvironment += "Gateway__OpcUa__CertificateStorePath=$resolvedDataDirectory\OpcUa\pki"
    $serviceEnvironment += "Gateway__Watchdog__StateDirectory=$resolvedDataDirectory\Watchdog"
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

    if (Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue) {
        Set-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $serviceEnvironment
    }
    else {
        New-ItemProperty -LiteralPath $serviceKey -Name Environment -PropertyType MultiString -Value $serviceEnvironment | Out-Null
    }
}

if ($EnableServiceRecovery) {
    $delayMs = [Math]::Max(1, $RestartDelaySeconds) * 1000
    $resetSeconds = [Math]::Max(1, $ResetFailureCountDays) * 86400
    if ($PSCmdlet.ShouldProcess($ServiceName, "Configure service recovery")) {
        sc.exe failure $ServiceName reset= $resetSeconds actions= restart/$delayMs/restart/$delayMs/none/$delayMs | Out-Null
        sc.exe failureflag $ServiceName 1 | Out-Null
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
