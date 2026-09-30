[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IsccPath,
    [string]$PackageDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
if (!$PackageDirectory) { $PackageDirectory = Join-Path $root 'dist/packages' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'dist/installers' }
$packages = [IO.Path]::GetFullPath($PackageDirectory)
$checks = @(Get-Content (Join-Path $packages 'checksums.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
$standard = @($checks | Where-Object { $_.Build.Variant -eq 'Standard' })
if ($standard.Count -ne 1) { throw 'Expected exactly one Standard package.' }
$item = $standard[0]
$version = [string]$item.Build.Version
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
if (!(Test-ReleaseVersion $version)) { throw 'Invalid package version.' }
$filename = "JTCStamper-$version-Standard-win-x64.zip"
if ($item.File -cne $filename) { throw 'Unexpected Standard package filename.' }
$zip = Join-Path $packages $filename
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $item.Sha256) { throw 'Standard package hash mismatch.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('JTCStamper-Installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $stage | Out-Null
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $allowed = @('JTCStamper.App.exe', 'README.txt', 'build.json', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.txt')
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        if ($names.Count -ne $allowed.Count -or (Compare-Object ($names | Sort-Object) ($allowed | Sort-Object))) { throw 'Unexpected Standard package contents.' }
        foreach ($entry in $archive.Entries) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage $entry.FullName))
        }
    } finally { $archive.Dispose() }
    $metadata = Get-Content (Join-Path $stage 'build.json') -Raw | ConvertFrom-Json
    if ($metadata.Version -ne $version -or $metadata.Variant -ne 'Standard' -or $metadata.Revision -ne $item.Build.Revision) { throw 'Package metadata mismatch.' }
    if ((Get-FileHash (Join-Path $stage 'JTCStamper.App.exe') -Algorithm SHA256).Hash -ne $item.Build.ExeSha256 -or $metadata.ExeSha256 -ne $item.Build.ExeSha256) { throw 'Package EXE hash mismatch.' }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Force $output | Out-Null
    & $IsccPath "/DSourceDir=$stage" "/DAppVersion=$version" "/DOutputDir=$output" (Join-Path $root 'installer/JTCStamper.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $setup = Join-Path $output "JTCStamper-$version-Standard-win-x64-Setup.exe"
    if (!(Test-Path -LiteralPath $setup)) { throw 'Installer was not produced.' }
    [pscustomobject]@{ File = [IO.Path]::GetFileName($setup); Sha256 = (Get-FileHash $setup -Algorithm SHA256).Hash; Build = $item.Build } |
        ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'checksums.json') -Encoding utf8
} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
