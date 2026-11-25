[CmdletBinding()]
param(
    [string]$ConfigRoot = "$env:ProgramData\IPCGateway\ha",
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
$wizard = Join-Path $PackageRoot 'components\IPC.Gateway.HaSetup\IPC.Gateway.HaSetup.dll'
if (!(Test-Path -LiteralPath $wizard -PathType Leaf)) { throw '请在解压后的完整安装包中打开组网配置。' }
& dotnet $wizard --configRoot ([IO.Path]::GetFullPath($ConfigRoot))
exit $LASTEXITCODE
