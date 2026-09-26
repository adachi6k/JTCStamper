[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublicCertificatePath)
$ErrorActionPreference = 'Stop'
# The private key stays in this Windows user's key store. Never add it to Git or a ZIP.
if (Test-Path -LiteralPath $PublicCertificatePath) { throw 'Public certificate output already exists.' }
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=JTC Stamper (Self-signed)' -FriendlyName 'JTC Stamper local code signing' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3)
Export-Certificate -Cert $cert -FilePath $PublicCertificatePath | Out-Null
Write-Host "Certificate thumbprint: $($cert.Thumbprint)"
Write-Host 'Private key is non-exportable and remains in CurrentUser\My. No trust stores were changed.'
