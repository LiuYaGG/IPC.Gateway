[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$NodeConfig,
    [string]$DataDirectory = "$env:ProgramData\IPCGatewayArbiter",
    [switch]$GatewayClient,
    [switch]$InstallService,
    [switch]$UsePublished,
    [string]$CertificatePasswordFile,
    [ValidateSet('IPCGatewayArbiter','IPCGatewayArbiterA','IPCGatewayArbiterB','IPCGatewayArbiterC')]
    [string]$ServiceName = 'IPCGatewayArbiter',
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
if ($CertificatePasswordFile) {
    if (!$InstallService -or !(Test-Path -LiteralPath $CertificatePasswordFile -PathType Leaf)) {
        throw 'Specify -InstallService and an existing access-controlled PFX password file.'
    }
    $passwordFilePath = (Resolve-Path -LiteralPath $CertificatePasswordFile).Path
}
if ($GatewayClient) {
    if ($InstallService -or $Start -or $NodeConfig -or $UsePublished) { throw 'Use the gateway installer for the business-node package.' }
    & (Join-Path $PSScriptRoot '生成安装升级包.ps1') -PackageType Install -RuntimeIdentifier win-x64 -OutputDirectory $PublishDirectory
    return
}
if ($Start -and !$InstallService) { throw 'Start requires InstallService.' }
$project = Join-Path $PSScriptRoot '..\..\src\IPC.Gateway.Arbiter\IPC.Gateway.Arbiter.csproj'
$publishPath = [IO.Path]::GetFullPath($PublishDirectory)
$dataPath = [IO.Path]::GetFullPath($DataDirectory)
if ($publishPath.TrimEnd('\') -eq $dataPath.TrimEnd('\') -or $dataPath.StartsWith($publishPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep the member state directory outside the publish directory.'
}
if ($InstallService) {
    if (!$NodeConfig) { throw 'Specify the provisioned node JSON using -NodeConfig.' }
    $configPath = (Resolve-Path -LiteralPath $NodeConfig).Path
    if (!(Test-Path -LiteralPath $configPath -PathType Leaf)) { throw 'NodeConfig must be a JSON file.' }
    $node=(Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json).Arbiter.NodeId
    if ($node -cne 'C' -or $ServiceName -notin @('IPCGatewayArbiter','IPCGatewayArbiterC')) { throw 'Standalone service installation is for C. Install the integrated gateway on A or B.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run PowerShell as administrator to register the service.'
    }
}
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$memberDisplayName = if ($ServiceName -eq 'IPCGatewayArbiter') { 'IPC Gateway Arbiter' } else { 'IPC Gateway Arbiter ' + $ServiceName.Substring('IPCGatewayArbiter'.Length) }
if ($existing -and $existing.Status -ne 'Stopped' -and $InstallService) {
    throw 'Transfer workloads, verify the other two voters, then stop this member before updating it.'
}
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
if ($UsePublished) {
    if (!$InstallService) { throw 'UsePublished is for installing an already built directory.' }
    foreach ($name in 'IPC.Gateway.Arbiter.dll','IPC.Gateway.Arbiter.runtimeconfig.json','IPC.Gateway.Arbiter.deps.json') {
        if (!(Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) { throw "Incomplete published payload: $name" }
    }
} else {
    if ((Test-Path -LiteralPath $publishPath) -and (Get-ChildItem -LiteralPath $publishPath -Force | Select-Object -First 1)) {
        throw 'Publish to a new empty version directory. Existing binaries and member state are preserved.'
    }
    & $dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}
if (!$InstallService) { Write-Output $publishPath; return }
$binary = '"{0}" "{1}" --contentRoot "{2}"' -f $dotnet,(Join-Path $publishPath 'IPC.Gateway.Arbiter.dll'),$publishPath
if ($existing) {
    if ($existing.DisplayName -ne $memberDisplayName) { throw 'An unrelated service already uses this name.' }
    $service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    $result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName=$binary; StartMode='Automatic' }
    if ($result.ReturnValue -ne 0) { throw "Service change failed: $($result.ReturnValue)" }
} else {
    New-Service -Name $serviceName -DisplayName $memberDisplayName -BinaryPathName $binary -StartupType Automatic | Out-Null
}
[IO.Directory]::CreateDirectory($dataPath) | Out-Null
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$old = (Get-ItemProperty -LiteralPath $regPath -Name Environment -ErrorAction SilentlyContinue).Environment
$envValues = @($old | Where-Object { $_ -and $_ -notmatch '^(IPC_ARBITER_CONFIG|IPC_ARBITER_SERVICE_NAME|Arbiter__StateDirectory)=' })
$envValues += "IPC_ARBITER_CONFIG=$configPath","IPC_ARBITER_SERVICE_NAME=$ServiceName","Arbiter__StateDirectory=$dataPath"
if ($CertificatePasswordFile) {
    $envValues = @($envValues | Where-Object { $_ -notmatch '^Arbiter__CertificatePassword(File)?=' })
    $envValues += "Arbiter__CertificatePasswordFile=$passwordFilePath"
}
New-ItemProperty -LiteralPath $regPath -Name Environment -PropertyType MultiString -Value $envValues -Force | Out-Null
if ($Start) { Start-Service -Name $serviceName }
Write-Output "Service $serviceName registered. State: $dataPath"
