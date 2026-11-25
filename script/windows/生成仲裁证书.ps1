#requires -Version 5.1
using namespace System.Security.Cryptography
using namespace System.Security.Cryptography.X509Certificates

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NodeA,
    [Parameter(Mandatory)][string]$NodeB,
    [Parameter(Mandatory)][string]$NodeC,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$addresses = [ordered]@{ A = $NodeA; B = $NodeB; C = $NodeC }
foreach ($address in $addresses.Values) {
    if ([Uri]::CheckHostName($address) -notin 'Dns', 'IPv4', 'IPv6') {
        throw "请填写端点实际使用的 IP 或 DNS 名称（不含协议和端口）：$address"
    }
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw "请选择新的输出目录，已有目录：$outputPath" }
$null = New-Item -ItemType Directory -Path $outputPath
$start = [DateTimeOffset]::UtcNow.AddMinutes(-5)

function New-Request([string]$name, [RSA]$rsa) {
    [CertificateRequest]::new("CN=$name", $rsa, [HashAlgorithmName]::SHA256, [RSASignaturePadding]::Pkcs1)
}
function Read-ExportPassword([string]$label) {
    $secret = Read-Host "$label 的 PFX 密码（请保存，至少 12 个字符）" -AsSecureString
    if ($secret.Length -lt 12) { throw "$label 的密码不足 12 个字符。" }
    return $secret
}

$caKey = [RSACng]::new(3072)
$rng = [RandomNumberGenerator]::Create()
try {
    $req = New-Request 'IPC Gateway HA Root' $caKey
    $req.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($true, $true, 0, $true))
    $req.CertificateExtensions.Add([X509KeyUsageExtension]::new([X509KeyUsageFlags]'KeyCertSign,CrlSign', $true))
    $req.CertificateExtensions.Add([X509SubjectKeyIdentifierExtension]::new($req.PublicKey, $false))
    $ca = $req.CreateSelfSigned($start, $start.AddYears(10))
    $offline = [IO.Directory]::CreateDirectory((Join-Path $outputPath 'CA-offline')).FullName
    $password = Read-ExportPassword '离线 CA'
    try {
        [IO.File]::WriteAllBytes((Join-Path $offline 'ca-backup.pfx'), $ca.Export([X509ContentType]::Pfx, $password))
    } finally { $password.Dispose() }
    $pins = foreach ($node in $addresses.Keys) {
        $folder = [IO.Directory]::CreateDirectory((Join-Path $outputPath $node)).FullName
        [IO.File]::WriteAllBytes((Join-Path $folder 'cluster-ca.cer'), $ca.RawData)
        $password = Read-ExportPassword "节点 $node"
        try {
            $roles = if ($node -eq 'C') { 'member' } else { 'member','gateway' }
            foreach ($role in $roles) {
                $name = "$role-$($node.ToLowerInvariant())"
                $key = [RSACng]::new(3072)
                try {
                    $req = New-Request $name $key
                    $req.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($false,$false,0,$true))
                    $req.CertificateExtensions.Add([X509KeyUsageExtension]::new([X509KeyUsageFlags]'DigitalSignature,KeyEncipherment',$true))
                    $uses = [OidCollection]::new()
                    $null = $uses.Add([Oid]::new('1.3.6.1.5.5.7.3.2'))
                    if ($role -eq 'member') {
                        $null = $uses.Add([Oid]::new('1.3.6.1.5.5.7.3.1'))
                        $san = [SubjectAlternativeNameBuilder]::new()
                        $ip = $null
                        if ([Net.IPAddress]::TryParse($addresses[$node], [ref]$ip)) { $san.AddIpAddress($ip) }
                        else { $san.AddDnsName($addresses[$node]) }
                        $req.CertificateExtensions.Add($san.Build())
                    }
                    $req.CertificateExtensions.Add([X509EnhancedKeyUsageExtension]::new($uses,$false))
                    $serial = [byte[]]::new(16)
                    $rng.GetBytes($serial)
                    $leaf = $req.Create($ca, $start, $start.AddYears(2), $serial)
                    $pfx = [RSACertificateExtensions]::CopyWithPrivateKey($leaf, $key)
                    try {
                        [IO.File]::WriteAllBytes((Join-Path $folder "$name.pfx"), $pfx.Export([X509ContentType]::Pfx,$password))
                        [pscustomobject]@{ Node=$node; File="$name.pfx"; Field= $(if ($role -eq 'member') {'CertificateSha256'} else {'GatewayCertificateSha256'}); SHA256=$leaf.GetCertHashString([HashAlgorithmName]::SHA256) }
                    } finally { $pfx.Dispose(); $leaf.Dispose() }
                } finally { $key.Dispose() }
            }
        } finally { $password.Dispose() }
    }
    $pins | Export-Csv -LiteralPath (Join-Path $outputPath 'fingerprints.csv') -NoTypeInformation -Encoding UTF8
    Write-Host "证书已生成：$outputPath"
} finally {
    if ($ca) { $ca.Dispose() }
    $rng.Dispose()
    $caKey.Dispose()
}
