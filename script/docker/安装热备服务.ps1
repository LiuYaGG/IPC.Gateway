[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('A','B','C')][string]$Role,
      [Parameter(Mandatory)][string]$EnvironmentFile,[switch]$Uninstall)
$ErrorActionPreference='Stop'
$Role=$Role.ToUpperInvariant()
$envFilePath=(Resolve-Path -LiteralPath $EnvironmentFile).Path
$nodeSuffix=$Role.ToLowerInvariant()
$oldArb=$env:IPC_ARBITER_NODE
$oldGw=$env:IPC_HA_NODE
function Invoke-RoleCompose([string]$Component,[string[]]$Arguments) {
    $name=if ($Component -eq 'arbiter') { "ipc-arbiter-$nodeSuffix" } else { "ipc-ha-gateway-$nodeSuffix" }
    $file=if ($Component -eq 'arbiter') { 'docker-compose.arbiter.yml' } else { 'docker-compose.ha-gateway.yml' }
    & docker compose -f (Join-Path $PSScriptRoot $file) --env-file $envFilePath -p $name @Arguments
    if ($LASTEXITCODE) { throw "Docker Compose failed for $Component." }
}
try {
    $env:IPC_ARBITER_NODE=$Role; $env:IPC_HA_NODE=$Role
    $component=if ($Role -eq 'C') { 'arbiter' } else { 'gateway' }
    Invoke-RoleCompose $component @('config','--quiet')
    if ($Uninstall) {
        Invoke-RoleCompose $component @('down')
    } else {
        if ($Role -ne 'C') {
            $legacy=& docker ps -aq --filter "label=com.docker.compose.project=ipc-arbiter-$nodeSuffix"
            if ($LASTEXITCODE) { throw 'Cannot check the existing arbiter containers.' }
            if ($legacy) { throw "Remove the old standalone arbiter $Role container and retain its volume before installing." }
        }
        if (Invoke-RoleCompose $component @('ps','--status','running','-q')) { throw "Stop the existing $Role $component container before updating." }
        Invoke-RoleCompose $component @('up','-d','--no-build')
    }
} finally { $env:IPC_ARBITER_NODE=$oldArb; $env:IPC_HA_NODE=$oldGw }
