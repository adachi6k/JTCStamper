[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$temp = Join-Path $env:TEMP ('jtc-signing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
$cert = $null
try {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=JTC ephemeral signing test' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(1)
    $exe = Join-Path $temp 'JTCStamper.App.exe'
    Copy-Item (Join-Path $root 'dist/lite-win-x64/JTCStamper.App.exe') $exe
    $result = & (Join-Path $PSScriptRoot 'Sign-ReleaseExecutable.ps1') -Path $exe -CertificateThumbprint $cert.Thumbprint
    if ($result.WindowsTrust -ne 'UntrustedRoot' -or $result.Thumbprint -ne $cert.Thumbprint) { throw 'Expected an untrusted but correctly signed EXE.' }
    $bytes = [IO.File]::ReadAllBytes($exe)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    $optionalSize = [BitConverter]::ToUInt16($bytes, $pe + 20)
    $raw = [BitConverter]::ToInt32($bytes, $pe + 24 + $optionalSize + 20)
    if ($raw -lt 1 -or $raw + 16 -ge $bytes.Length) { throw 'Invalid PE section.' }
    $bytes[$raw + 16] = $bytes[$raw + 16] -bxor 1
    [IO.File]::WriteAllBytes($exe, $bytes)
    $bad = [JtcAuthenticodeVerifier]::Verify($exe)
    if ($bad -eq 0 -or $bad -eq [uint32]2148204809) { throw 'Modified EXE was accepted.' }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Sign-ReleaseExecutable.ps1') -Path $exe -CertificateThumbprint 'invalid' | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid signer was accepted.' }
    Write-Host ('PASS self-signature / untrusted-root isolation / modified-EXE rejection (0x{0:X8}) / invalid signer' -f $bad)
} finally {
    if ($cert) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey }
    Remove-Item -LiteralPath $temp -Recurse -Force
}
