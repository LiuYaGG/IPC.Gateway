[CmdletBinding(DefaultParameterSetName='Member')]
param(
    [Parameter(Mandatory,ParameterSetName='Member')][Parameter(Mandatory,ParameterSetName='DeployGateway')][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$')][string]$NodeId,
    [Parameter(Mandatory,ParameterSetName='Member')][Parameter(Mandatory,ParameterSetName='DeployGateway')][string]$ConfigDirectory,
    [Parameter(Mandatory)][string]$Image,
    [Parameter(ParameterSetName='Member')][switch]$NoBuild,
    [Parameter(Mandatory,ParameterSetName='Gateway')][switch]$GatewayClient,
    [Parameter(Mandatory,ParameterSetName='DeployGateway')][switch]$DeployGateway,
    [Parameter(Mandatory,ParameterSetName='DeployGateway')][string]$DataDirectory,
    [Parameter(Mandatory,ParameterSetName='DeployGateway')][string]$PeerHistoryDirectory,
    [Parameter(Mandatory,ParameterSetName='DeployGateway')][string]$PeerColdDirectory
)
$ErrorActionPreference = 'Stop'
if ($GatewayClient) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    & docker build -f (Join-Path $PSScriptRoot 'Dockerfile') -t $Image $root
    if ($LASTEXITCODE -ne 0) { throw 'Gateway image build failed.' }
    return
}
$directory = (Resolve-Path -LiteralPath $ConfigDirectory).Path
$member = $NodeId.ToLowerInvariant()
if ($DeployGateway) {
    if ($NodeId -cnotin 'A','B') { throw 'The business node ID must be A or B.' }
    $legacy=& docker ps -aq --filter "label=com.docker.compose.project=ipc-arbiter-$member"
    if ($LASTEXITCODE) { throw 'Cannot check the existing arbiter containers.' }
    if ($legacy) { throw 'Remove the old standalone arbiter container and retain its volume before installing.' }
    if (!(Test-Path -LiteralPath (Join-Path $directory 'appsettings.Production.json') -PathType Leaf)) { throw 'appsettings.Production.json is missing.' }
    $values = @{IPC_HA_GATEWAY_CONFIG_DIR=$directory; IPC_HA_GATEWAY_IMAGE=$Image; IPC_HA_NODE=$NodeId}
    foreach ($pair in @{IPC_HA_GATEWAY_DATA=$DataDirectory; IPC_HA_PEER_HISTORY=$PeerHistoryDirectory; IPC_HA_PEER_COLD=$PeerColdDirectory}.GetEnumerator()) {
        if (!(Test-Path -LiteralPath $pair.Value -PathType Container)) { throw "Directory does not exist: $($pair.Value)" }
        $values[$pair.Key] = (Resolve-Path -LiteralPath $pair.Value).Path
    }
    $data = $values.IPC_HA_GATEWAY_DATA.Replace('\','/').TrimEnd('/') + '/'
    foreach ($path in $directory,$values.IPC_HA_PEER_HISTORY,$values.IPC_HA_PEER_COLD) {
        $other = $path.Replace('\','/').TrimEnd('/') + '/'
        if ($other.StartsWith($data,[StringComparison]::OrdinalIgnoreCase) -or $data.StartsWith($other,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Keep configuration, local data and remote history on separate paths.'
        }
    }
    $compose = 'docker-compose.ha-gateway.yml'
    $project = "ipc-ha-gateway-$member"
    $service = 'gateway'
} else {
    if ($member -ne 'c') { throw 'Standalone member deployment is for C. Install the integrated gateway on A or B.' }
    if (!(Test-Path -LiteralPath (Join-Path $directory 'member.json') -PathType Leaf)) { throw 'member.json is missing.' }
    $values = @{IPC_ARBITER_CONFIG_DIR=$directory; IPC_ARBITER_IMAGE=$Image; IPC_ARBITER_NODE=$member.ToUpperInvariant(); IPC_ARBITER_VOLUME="ipc-arbiter-$member-state"}
    $compose = 'docker-compose.arbiter.yml'
    $project = "ipc-arbiter-$member"
    $service = 'arbiter'
}
$original = @{}
try {
    foreach ($key in $values.Keys) {
        $original[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key,$values[$key])
    }
    $options = @('-f',(Join-Path $PSScriptRoot $compose),'-p',$project)
    & docker compose @options config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Invalid Docker Compose configuration.' }
    $running = & docker compose @options ps --status running -q $service
    if ($LASTEXITCODE -ne 0) { throw 'Cannot query Docker Compose.' }
    if ($running) { throw 'Verify the other two voters, then stop this member before updating.' }
    $build = if ($NoBuild -or $DeployGateway) { '--no-build' } else { '--build' }
    & docker compose @options up -d $build $service
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed.' }
} finally {
    foreach ($key in $original.Keys) { [Environment]::SetEnvironmentVariable($key,$original[$key]) }
}
