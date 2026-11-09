[CmdletBinding()]
param(
    [string]$RepositoryRoot = '',
    [string]$ComposeFile = '',
    [string]$EnvironmentFile = '',
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$ProjectName = 'ipc-gateway',
    [string]$Image = 'ipc-gateway:latest',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (!$RepositoryRoot) { $RepositoryRoot = Join-Path $PSScriptRoot '../..' }
if (!$ComposeFile) { $ComposeFile = Join-Path $PSScriptRoot 'docker-compose.external-db.yml' }
if (!$EnvironmentFile) { $EnvironmentFile = Join-Path $PSScriptRoot 'gateway.env' }
$ComposeFile = (Resolve-Path -LiteralPath $ComposeFile).Path
if (!(Test-Path -LiteralPath $EnvironmentFile -PathType Leaf)) { throw 'Copy gateway.env.example to gateway.env and configure the database and credentials first.' }
$EnvironmentFile = (Resolve-Path -LiteralPath $EnvironmentFile).Path
if (!$NoBuild -and !(Test-Path -LiteralPath (Join-Path $RepositoryRoot 'script/docker/Dockerfile') -PathType Leaf)) { throw 'Building requires a source checkout. Set -RepositoryRoot, or load a release image and use -NoBuild.' }
$docker = (Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$previousSource = $env:IPC_GATEWAY_SOURCE_DIRECTORY
$previousImage = $env:IPC_GATEWAY_IMAGE
$previousEnv = $env:IPC_GATEWAY_ENV_FILE
try {
    $env:IPC_GATEWAY_SOURCE_DIRECTORY = [IO.Path]::GetFullPath($RepositoryRoot).Replace('\','/')
    $env:IPC_GATEWAY_IMAGE = $Image
    $env:IPC_GATEWAY_ENV_FILE = $EnvironmentFile.Replace('\','/')
    & $docker compose version
    if ($LASTEXITCODE) { throw 'Docker Compose v2 is required.' }
    & $docker compose -p $ProjectName --env-file $EnvironmentFile -f $ComposeFile config --quiet
    if ($LASTEXITCODE) { throw 'Invalid Docker Compose configuration.' }
    $arguments = @('compose','-p',$ProjectName,'--env-file',$EnvironmentFile,'-f',$ComposeFile,'up','-d')
    if (!$NoBuild) { $arguments += '--build' } else { $arguments += '--no-build' }
    & $docker @arguments
    if ($LASTEXITCODE) { throw "Docker deployment failed: $LASTEXITCODE" }
    Write-Host 'Containers started. Check container logs and /health/ready before putting the gateway into service.'
}
finally {
    $env:IPC_GATEWAY_SOURCE_DIRECTORY = $previousSource
    $env:IPC_GATEWAY_IMAGE = $previousImage
    $env:IPC_GATEWAY_ENV_FILE = $previousEnv
}
