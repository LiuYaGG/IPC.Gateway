[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = "IPCGateway",
    [string]$DisplayName = "IPC Gateway Edge Service",
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$Url = "http://127.0.0.1:5184",
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
$environment = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "DOTNET_ENVIRONMENT=Production",
    "ASPNETCORE_URLS=$Url"
)

if ($PSCmdlet.ShouldProcess($ServiceName, "Set service environment variables")) {
    if (-not (Test-Path -LiteralPath $serviceKey)) {
        throw "Service registry key '$serviceKey' was not found."
    }

    if (Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue) {
        Set-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $environment
    }
    else {
        New-ItemProperty -LiteralPath $serviceKey -Name Environment -PropertyType MultiString -Value $environment | Out-Null
    }
}

if ($Start) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Start Windows service")) {
        Start-Service -Name $ServiceName
    }
}

Write-Host "Windows service '$ServiceName' is configured for $dllPath"
