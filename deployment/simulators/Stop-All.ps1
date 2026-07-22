$ErrorActionPreference = 'Stop'
$root = 'D:\IPC-Simulators'
$stopFile = Join-Path $root 'stop.requested'
Set-Content -LiteralPath $stopFile -Value ([DateTime]::UtcNow.ToString('O')) -Encoding ascii

$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ((Test-Path (Join-Path $root 'state.json')) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
}

if (Test-Path (Join-Path $root 'state.json')) {
    throw 'Simulator supervisor did not stop within 10 seconds. Check logs under D:\IPC-Simulators\logs.'
}

Write-Host 'All IPC protocol simulators stopped.'
