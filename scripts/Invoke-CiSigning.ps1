[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$ExpectedRevision)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($ExpectedRevision -notmatch '\A[0-9a-f]{40}\z') { throw 'Expected a source commit SHA.' }
foreach ($name in @('JTC_SIGNING_PFX','JTC_SIGNING_PASSWORD','JTC_SIGNING_CERT_SHA256')) {
    if (![Environment]::GetEnvironmentVariable($name)) { throw "Missing signing configuration: $name" }
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Signing output must be a new directory.' }
$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$temp = Join-Path $env:RUNNER_TEMP ('jtc-sign-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp, $outputRoot | Out-Null
$imported = @()
$complete = $false
try {
    $pfx = Join-Path $temp 'signer.pfx'
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:JTC_SIGNING_PFX))
    $password = ConvertTo-SecureString $env:JTC_SIGNING_PASSWORD -AsPlainText -Force
    $imported = @(Import-PfxCertificate -FilePath $pfx -Password $password -CertStoreLocation Cert:\CurrentUser\My)
    $certs = @($imported | Where-Object { $_.HasPrivateKey })
    if ($certs.Count -ne 1) { throw 'Expected exactly one signing key.' }
    $cert = $certs[0]
    $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cert.RawData))
    if ($sha -ne $env:JTC_SIGNING_CERT_SHA256) { throw 'Unexpected signing certificate fingerprint.' }
    Remove-Item -LiteralPath $pfx -Force
    $env:JTC_SIGNING_PFX = $null; $env:JTC_SIGNING_PASSWORD = $null
    $password.Dispose()
    $checks = @(Get-Content (Join-Path $inputRoot 'checksums.json') -Raw | ConvertFrom-Json)
    if ($checks.Count -ne 2 -or (@($checks.Build.Variant | Sort-Object) -join ',') -ne 'Lite,Standard') { throw 'Expected both editions.' }
    $allow = @('JTCStamper.App.exe','README.txt','build.json','LICENSE','NOTICE','THIRD-PARTY-NOTICES.txt')
    $signed = @()
    foreach ($item in $checks) {
        if ([IO.Path]::GetFileName($item.File) -cne $item.File) { throw 'Invalid input filename.' }
        $zip = Join-Path $inputRoot $item.File
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $item.Sha256) { throw 'Input ZIP hash mismatch.' }
        $stage = Join-Path $temp $item.Build.Variant
        New-Item -ItemType Directory $stage | Out-Null
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            $names = @($archive.Entries | ForEach-Object { $_.FullName })
            if ($names.Count -ne 6 -or (Compare-Object ($names | Sort-Object) ($allow | Sort-Object))) { throw 'Unexpected archive contents.' }
            foreach ($entry in $archive.Entries) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage $entry.FullName)) }
        } finally { $archive.Dispose() }
        $metadata = Get-Content (Join-Path $stage 'build.json') -Raw | ConvertFrom-Json
        . (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
        if (!(Test-ReleaseVersion $metadata.Version) -or $metadata.Revision -ne $ExpectedRevision -or $metadata.Variant -ne $item.Build.Variant -or $metadata.Version -ne $item.Build.Version) { throw 'Input source/version mismatch.' }
        if ($item.File -cne "JTCStamper-$($metadata.Version)-$($metadata.Variant)-win-x64.zip") { throw 'Unexpected package name.' }
        $exe = Join-Path $stage 'JTCStamper.App.exe'
        $before = (Get-FileHash $exe -Algorithm SHA256).Hash
        if ($before -ne $metadata.ExeSha256 -or $before -ne $item.Build.ExeSha256) { throw 'Input EXE hash mismatch.' }
        $metadata.Signing = & (Join-Path $PSScriptRoot 'Sign-ReleaseExecutable.ps1') -Path $exe -CertificateThumbprint $cert.Thumbprint
        $metadata.ExeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash
        $metadata.ExeBytes = (Get-Item $exe).Length
        $metadata | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $stage 'build.json') -Encoding utf8
        $guide = Join-Path $stage 'README.txt'
        $text = Get-Content $guide -Raw
        Set-Content $guide ("Self-signed. Windows trust is not automatic; SmartScreen warnings may remain. No timestamp. Signer: $($cert.Thumbprint)`r`n`r`n" + $text) -Encoding utf8
        $target = Join-Path $outputRoot $item.File
        Compress-Archive -LiteralPath @($allow | ForEach-Object { Join-Path $stage $_ }) -DestinationPath $target
        $signed += [pscustomobject]@{ File=$item.File; Sha256=(Get-FileHash $target -Algorithm SHA256).Hash; Build=$metadata }
    }
    $signed | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputRoot 'checksums.json') -Encoding utf8
    Export-Certificate -Cert $cert -FilePath (Join-Path $outputRoot 'JTCStamper-signing.cer') | Out-Null
    $complete = $true
} finally {
    $env:JTC_SIGNING_PFX = $null; $env:JTC_SIGNING_PASSWORD = $null
    foreach ($c in $imported) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($c.Thumbprint)" -DeleteKey -ErrorAction Continue }
    Remove-Item -LiteralPath $temp -Recurse -Force
    if (!$complete) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
}
