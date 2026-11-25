[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('A','B','C')][string]$Role,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$ConfigRoot,
    [string]$PackageRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$MemberConfig, [string]$GatewayConfig, [string]$MemberData,
    [string]$DataDirectory, [string]$Url, [string]$CertificatePasswordFile, [string]$PackageZip, [string]$UpdateDirectory,
    [switch]$Start, [switch]$StartAsStandalone
)
$ErrorActionPreference='Stop'
$Role=$Role.ToUpperInvariant()
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator.' }
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot)
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot).Path
$ConfigRoot=[IO.Path]::GetFullPath($ConfigRoot).TrimEnd('\')
$nodeDir=Join-Path $ConfigRoot $Role
$standaloneMarker=Join-Path $ConfigRoot 'standalone-before-setup'
if ($StartAsStandalone) {
    if ($Role -cne 'A') { throw 'StartAsStandalone is for an initial installation of A only.' }
    if (Get-Service -Name 'IPCGateway','IPCGatewayArbiter','IPCGatewayArbiterA','IPCGatewayArbiterB','IPCGatewayArbiterC' -ErrorAction SilentlyContinue) { throw 'StartAsStandalone requires a new A installation without a registered gateway or arbiter.' }
}
$name="IPCGatewayArbiter$Role"
function Read-ServiceEnvironment([string]$Service) {
    $map=@{}
    foreach ($entry in (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$Service" -ErrorAction SilentlyContinue).Environment) {
        if ($entry -match '^([^=]+)=(.*)$') { $map[$Matches[1]]=$Matches[2] }
    }
    return $map
}
if ($Role -eq 'C') {
    if (!$MemberConfig) { $MemberConfig=Join-Path $nodeDir 'member.json' }
    $MemberConfig=(Resolve-Path -LiteralPath $MemberConfig).Path
    $member=(Get-Content -LiteralPath $MemberConfig -Raw -Encoding UTF8 | ConvertFrom-Json).Arbiter
    if ($member.NodeId -cne $Role) { throw 'The selected member.json belongs to another role.' }
} elseif ($MemberConfig -or $MemberData -or $GatewayConfig -or $CertificatePasswordFile) {
    throw 'A/B use identities in ConfigRoot. Back up and migrate legacy HA state before replacing the old member; do not supply standalone member arguments.'
}
foreach ($candidate in @('IPCGatewayArbiter',"IPCGatewayArbiter$Role")) {
    $svc=Get-Service -Name $candidate -ErrorAction SilentlyContinue
    if (!$svc) { continue }
    if ($Role -ne 'C' -and $candidate -eq "IPCGatewayArbiter$Role") { throw "先卸载原独立仲裁服务 $candidate，再安装集成网关。保留原成员状态。" }
    $envMap=Read-ServiceEnvironment $candidate
    $oldConfig=$envMap['IPC_ARBITER_CONFIG']
    if (!$oldConfig -or !(Test-Path -LiteralPath $oldConfig -PathType Leaf)) { throw "Cannot identify existing member $candidate. Restore its registered member.json first." }
    $oldMember=(Get-Content -LiteralPath $oldConfig -Raw -Encoding UTF8 | ConvertFrom-Json).Arbiter
    if ($oldMember.NodeId -cne $Role) {
        if ($candidate -eq "IPCGatewayArbiter$Role") { throw "Conflicting member identity on $candidate" }
        continue
    }
    if ($Role -ne 'C') { throw "先卸载原独立仲裁服务 $candidate，再安装集成网关。保留原成员状态。" }
    if ($name -ne "IPCGatewayArbiter$Role") { throw "Two services already exist for member $Role." }
    $name=$candidate
    if ($svc.Status -ne 'Stopped') { throw "Stop $candidate before updating." }
    $savedState=$envMap['Arbiter__StateDirectory']
    if (!$savedState) { $savedState=$oldMember.StateDirectory }
    if ($savedState -and $MemberData -and [IO.Path]::GetFullPath($MemberData) -ne [IO.Path]::GetFullPath($savedState)) { throw 'Existing member data path differs from the requested path.' }
    if ($savedState) { $MemberData=$savedState }
}
if ($Role -eq 'C') {
    if (!$MemberData) { $MemberData=$member.StateDirectory }
    if (!$MemberData -or ![IO.Path]::IsPathRooted($MemberData)) { throw 'Specify the existing absolute member state directory with -MemberData.' }
    $identityPath=Join-Path $MemberData 'identity.json'
    if (Test-Path -LiteralPath $identityPath) {
        $identity=Get-Content -LiteralPath $identityPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($identity.NodeId -cne $Role -or $identity.ClusterId -cne $member.ClusterId) { throw 'Member data belongs to a different node or cluster.' }
    }
}
$app=Join-Path $InstallRoot 'app'
$arbiter=Join-Path $InstallRoot "arbiter-$Role"
$arbPath=[IO.Path]::GetFullPath($arbiter).TrimEnd('\')+'\'
$state=if ($Role -eq 'C') { $MemberData } else { $ConfigRoot }
$statePath=[IO.Path]::GetFullPath($state).TrimEnd('\')+'\'
if ($arbPath.StartsWith($statePath,[StringComparison]::OrdinalIgnoreCase) -or $statePath.StartsWith($arbPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the member program and its state in separate directories.' }
function Copy-Program([string]$Source,[string]$Destination,[string]$Dll) {
    if (!(Test-Path -LiteralPath (Join-Path $Source $Dll) -PathType Leaf)) { throw "Missing published component $Dll" }
    $from=[IO.Path]::GetFullPath($Source).TrimEnd('\')+'\'
    $to=[IO.Path]::GetFullPath($Destination).TrimEnd('\')+'\'
    if ($from.StartsWith($to,[StringComparison]::OrdinalIgnoreCase) -or $to.StartsWith($from,[StringComparison]::OrdinalIgnoreCase)) { throw 'Extract the package outside the installation directory.' }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Where-Object { $_.Name -notin @('Data','logs','appsettings.Production.json') } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force }
}
if ($Role -ne 'C') {
    if (!(Test-Path -LiteralPath (Join-Path $PackageRoot 'payload\IPC.Gateway.Arbiter.dll') -PathType Leaf)) { throw 'The gateway payload does not contain the integrated arbiter. Use the new installation package.' }
    $gw=Get-Service -Name IPCGateway -ErrorAction SilentlyContinue
    if ($gw -and ($gw.Status -ne 'Stopped' -or $gw.DisplayName -ne 'IPC Gateway Edge Service')) { throw 'Stop the existing IPC Gateway Edge Service before updating.' }
    $existing=Read-ServiceEnvironment IPCGateway
    if ($existing['Gateway__EmbeddedArbiter__Role'] -and ($existing['Gateway__EmbeddedArbiter__Role'] -cne $Role -or $existing['Gateway__EmbeddedArbiter__ConfigRoot'] -ne $ConfigRoot)) { throw 'Existing integrated gateway belongs to another role or identity directory.' }
    $appPath=[IO.Path]::GetFullPath($app).TrimEnd('\')+'\'
    if ($appPath.StartsWith($statePath,[StringComparison]::OrdinalIgnoreCase) -or $statePath.StartsWith($appPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Use a dedicated identity directory separate from the gateway program.' }
    if (Test-Path -LiteralPath $ConfigRoot) {
        $dir=Get-Item -Force -LiteralPath $ConfigRoot
        if (!$dir.PSIsContainer -or ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Use a dedicated local identity directory.' }
        $entries=@(Get-ChildItem -Force -LiteralPath $ConfigRoot -ErrorAction Stop)
        if ($StartAsStandalone -and ($entries | Where-Object Name -ne 'standalone-before-setup')) { throw 'StartAsStandalone requires an empty, unprovisioned identity directory.' }
        if (!$existing['Gateway__EmbeddedArbiter__Role'] -and ($entries | Where-Object Name -notin @($Role,'.setup.lock','setup-complete','standalone-before-setup'))) { throw 'ConfigRoot contains unrelated entries. Select a directory dedicated to this gateway role.' }
    }
    if ($StartAsStandalone) {
        $legacyRoots=@((Join-Path $InstallRoot 'ha'), (Join-Path $InstallRoot 'ha-data'), (Join-Path $app 'Data\HighAvailability'))
        if ($DataDirectory) { $legacyRoots+=@((Join-Path $DataDirectory 'HighAvailability'), (Join-Path $DataDirectory 'ha-gateway-A')) }
        foreach ($path in $legacyRoots) {
            if (([IO.Path]::GetFullPath($path) -ne $ConfigRoot) -and (Test-Path -LiteralPath $path) -and (Get-ChildItem -Force -LiteralPath $path -ErrorAction Stop | Select-Object -First 1)) { throw "Existing HA state detected at $path. Use the existing-cluster deployment procedure." }
        }
        $prod=Join-Path $app 'appsettings.Production.json'
        if (Test-Path -LiteralPath $prod -PathType Leaf) {
            $oldHA=(Get-Content -Raw -Encoding UTF8 -LiteralPath $prod | ConvertFrom-Json).Gateway.HighAvailability
            if ($oldHA.ClusterId -or $oldHA.NodeId -or $oldHA.Endpoints -or $oldHA.Mode -eq 'Cluster') { throw 'Production configuration already registers a high-availability identity.' }
        }
    }
    if (!$Url) { $Url=$existing['ASPNETCORE_URLS']; if (!$Url) { $Url='http://127.0.0.1:5184' } }
    if ($PackageZip -and (Test-Path -LiteralPath (Join-Path $app 'IPC.Gateway.WebHost.dll'))) {
        if (!$UpdateDirectory) { $UpdateDirectory=$existing['Gateway__Maintenance__Updates__UpdateDirectory'] }
        if (!$UpdateDirectory -and $DataDirectory) { $UpdateDirectory=Join-Path $DataDirectory 'Updates' }
        if (!$UpdateDirectory) { $UpdateDirectory=Join-Path $app 'Data\Updates' }
        & dotnet (Join-Path $PackageRoot 'payload\IPC.Gateway.WebHost.dll') --prepare-driver-trust (Resolve-Path -LiteralPath $PackageZip).Path $app ([IO.Path]::GetFullPath($UpdateDirectory))
        if ($LASTEXITCODE -ne 0) { throw 'Preparing driver trust failed; no program files were replaced.' }
    }
    $serviceUser=if ($gw) { (Get-CimInstance Win32_Service -Filter "Name='IPCGateway'").StartName } else { 'LocalSystem' }
    $identities=@([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'),[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))
    if ($serviceUser -ne 'LocalSystem') { $identities+=([Security.Principal.NTAccount]::new($serviceUser)).Translate([Security.Principal.SecurityIdentifier]) }
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($identity in $identities) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    $null=New-Item -ItemType Directory -Path $ConfigRoot -Force
    Set-Acl -LiteralPath $ConfigRoot -AclObject $acl
    Copy-Program (Join-Path $PackageRoot 'payload') $app 'IPC.Gateway.WebHost.dll'
    if ($StartAsStandalone) {
        if (Test-Path -LiteralPath $standaloneMarker) {
            $item=Get-Item -Force -LiteralPath $standaloneMarker
            if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [IO.File]::ReadAllText($standaloneMarker) -cne 'A') { throw 'Invalid standalone provisioning marker.' }
        } else {
            $stream=[IO.File]::Open($standaloneMarker,'CreateNew','Write','None')
            try { $stream.WriteByte(65); $stream.Flush($true) } finally { $stream.Dispose() }
        }
    }
    $overrides=@("Gateway__EmbeddedArbiter__Role=$Role","Gateway__EmbeddedArbiter__ConfigRoot=$ConfigRoot",'IPC_GATEWAY_HA_CONFIG=')
    & (Join-Path $PSScriptRoot '安装Windows服务.ps1') -PublishDirectory $app -Url $Url -DataDirectory $DataDirectory -Environment $overrides -EnableServiceRecovery
    if ($Start) { Start-Service IPCGateway }
    Write-Host "Installed IPCGateway with integrated member $Role. Identity directory: $ConfigRoot"
} else {
    Copy-Program (Join-Path $PackageRoot 'components\IPC.Gateway.Arbiter') $arbiter 'IPC.Gateway.Arbiter.dll'
    & (Join-Path $PSScriptRoot '发布仲裁客户端.ps1') -PublishDirectory $arbiter -ServiceName $name -UsePublished -InstallService -NodeConfig $MemberConfig -DataDirectory $MemberData -CertificatePasswordFile $CertificatePasswordFile
    if ($Start) { Start-Service $name }
    Write-Host "Installed member C as $name. State: $MemberData"
}
