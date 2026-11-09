[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ComposeFile = '',
    [string]$EnvironmentFile = '',
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$ProjectName = 'ipc-gateway'
)
$ErrorActionPreference = 'Stop'
if (!$ComposeFile) { $ComposeFile = Join-Path $PSScriptRoot 'docker-compose.external-db.yml' }
if (!$EnvironmentFile) { $EnvironmentFile = Join-Path $PSScriptRoot 'gateway.env' }
$ComposeFile = (Resolve-Path -LiteralPath $ComposeFile).Path
$EnvironmentFile = (Resolve-Path -LiteralPath $EnvironmentFile).Path
$previousEnv = $env:IPC_GATEWAY_ENV_FILE
try {
    $env:IPC_GATEWAY_ENV_FILE = $EnvironmentFile.Replace('\','/')
    if ($PSCmdlet.ShouldProcess($ProjectName, 'Remove Docker Compose containers and network; retain images, volumes and database')) {
        & docker compose -p $ProjectName --env-file $EnvironmentFile -f $ComposeFile down
        if ($LASTEXITCODE) { throw "Docker removal failed: $LASTEXITCODE" }
    }
}
finally { $env:IPC_GATEWAY_ENV_FILE = $previousEnv }
Write-Host 'Persistent volumes, images, environment file and external database were retained.'
