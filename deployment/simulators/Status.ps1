$root = 'D:\IPC-Simulators'
$stateFile = Join-Path $root 'state.json'
$ports = @(
    @{ Protocol = 'Modbus TCP'; Transport = 'TCP'; Port = 1502 },
    @{ Protocol = 'Siemens S7'; Transport = 'TCP'; Port = 1102 },
    @{ Protocol = 'BACnet/IP'; Transport = 'UDP'; Port = 47808 },
    @{ Protocol = 'EtherNet/IP'; Transport = 'TCP/UDP'; Port = 44818 },
    @{ Protocol = 'SNMP v2c'; Transport = 'UDP'; Port = 1161 },
    @{ Protocol = 'DNP3'; Transport = 'TCP'; Port = 20000 }
)

if (Test-Path $stateFile) {
    $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    $rows = foreach ($property in $state.children.PSObject.Properties) {
        $process = Get-Process -Id ([int]$property.Value) -ErrorAction SilentlyContinue
        [pscustomobject]@{
            Simulator = $property.Name
            PID = $property.Value
            Running = [bool]$process
        }
    }
    $rows | Format-Table -AutoSize
} else {
    Write-Warning 'No simulator state file found.'
}

$ports | ForEach-Object {
    $listeners = Get-NetTCPConnection -State Listen -LocalPort $_.Port -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Protocol = $_.Protocol
        Transport = $_.Transport
        Port = $_.Port
        TcpListening = [bool]$listeners
    }
} | Format-Table -AutoSize
