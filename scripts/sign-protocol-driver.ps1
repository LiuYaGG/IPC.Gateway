[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,
    [string]$ManifestPath = "",
    [Parameter(Mandatory = $true)]
    [string]$SigningPrivateKeyPath,
    [string]$Signer = ""
)

$ErrorActionPreference = "Stop"

function Get-TextValue {
    param(
        [object]$Source,
        [string]$Name,
        [string]$Fallback = ""
    )

    if ($null -eq $Source) {
        return $Fallback
    }

    if ($Source -is [System.Collections.IDictionary]) {
        if (-not $Source.Contains($Name) -or $null -eq $Source[$Name]) {
            return $Fallback
        }

        $dictionaryText = [string]$Source[$Name]
        if ([string]::IsNullOrWhiteSpace($dictionaryText)) {
            return $Fallback
        }

        return $dictionaryText
    }

    $property = $Source.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $Fallback
    }

    $text = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Fallback
    }

    return $text
}

function Get-DriverSigningPayload {
    param(
        [object]$Manifest,
        [string]$AssemblySha256
    )

    $lines = @(
        (Get-TextValue -Source $Manifest -Name "DriverId"),
        (Get-TextValue -Source $Manifest -Name "Version"),
        $AssemblySha256.ToLowerInvariant(),
        (Get-TextValue -Source $Manifest -Name "MinGatewayVersion"),
        (Get-TextValue -Source $Manifest -Name "MaxGatewayVersion")
    )
    return [string]::Join("`n", $lines)
}

$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $directory = Split-Path -Parent $resolvedAssembly
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedAssembly)
    $ManifestPath = Join-Path $directory "$fileName.ipc-driver.json"
}
$resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
$resolvedKey = (Resolve-Path -LiteralPath $SigningPrivateKeyPath).Path

$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($resolvedAssembly)
$manifest = $null
if (Test-Path -LiteralPath $resolvedManifest -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
}

$defaultDisplayName = if ([string]::IsNullOrWhiteSpace($assemblyName.Name)) { "" } else { $assemblyName.Name }
$defaultVersion = if ($null -eq $assemblyName.Version) { "0.0.0.0" } else { $assemblyName.Version.ToString() }
$sha256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestObject = [ordered]@{
    DriverId = Get-TextValue -Source $manifest -Name "DriverId" -Fallback ([System.IO.Path]::GetFileNameWithoutExtension($resolvedAssembly))
    DisplayName = Get-TextValue -Source $manifest -Name "DisplayName" -Fallback $defaultDisplayName
    Version = Get-TextValue -Source $manifest -Name "Version" -Fallback $defaultVersion
    MinGatewayVersion = Get-TextValue -Source $manifest -Name "MinGatewayVersion"
    MaxGatewayVersion = Get-TextValue -Source $manifest -Name "MaxGatewayVersion"
    Assembly = Get-TextValue -Source $manifest -Name "Assembly" -Fallback ([System.IO.Path]::GetFileName($resolvedAssembly))
    EntryType = Get-TextValue -Source $manifest -Name "EntryType"
    AssemblySha256 = $sha256
    Signature = ""
    SignatureAlgorithm = "RS256"
    Signer = if ([string]::IsNullOrWhiteSpace($Signer)) { [Environment]::UserName } else { $Signer }
}

$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $pem = [System.IO.File]::ReadAllText($resolvedKey)
    $rsa.ImportFromPem($pem)
    $payload = Get-DriverSigningPayload -Manifest $manifestObject -AssemblySha256 $sha256
    $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $signatureBytes = $rsa.SignData(
        $payloadBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $manifestObject["Signature"] = [Convert]::ToBase64String($signatureBytes)
}
catch [System.Management.Automation.MethodException] {
    throw "Protocol driver signing requires a PowerShell runtime that supports RSA.ImportFromPem. Use PowerShell 7+ or sign in CI."
}
finally {
    $rsa.Dispose()
}

$directory = Split-Path -Parent $resolvedManifest
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$manifestObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedManifest -Encoding UTF8
Write-Host "Manifest: $resolvedManifest"
Write-Host "SHA256  : $sha256"
Write-Host "Signer  : $($manifestObject.Signer)"
