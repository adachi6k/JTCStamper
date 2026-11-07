[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'MsixIdentity.ps1')
function New-TestManifest { return [xml](Get-Content (Join-Path $root 'packaging/AppxManifest.xml') -Raw) }
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid MSIX input was accepted.' }
}
$test = Set-MsixIdentity -Manifest (New-TestManifest) -Mode Test -ProductVersion '1.2.3'
if ($test.Package.Identity.Version -ne '1.2.3.0' -or $test.Package.Identity.Name -ne 'JTCStamper.LocalTest') {
    throw 'Test identity/version mismatch.'
}
$identity = [pscustomobject]@{ Name = '12345.ExampleApp'; Publisher = 'CN=00000000-0000-0000-0000-000000000001'; PublisherDisplayName = 'Example & Developer' }
$store = Set-MsixIdentity -Manifest (New-TestManifest) -Mode Store -ProductVersion '0.2.1' -StoreIdentity $identity
$roundTrip = [xml]$store.OuterXml
if ($roundTrip.Package.Identity.Name -ne $identity.Name -or
    $roundTrip.Package.Identity.Publisher -ne $identity.Publisher -or
    $roundTrip.Package.Properties.PublisherDisplayName -ne $identity.PublisherDisplayName -or
    $roundTrip.Package.Applications.Application.VisualElements.DisplayName -ne 'JTC Stamper') {
    throw 'Store identity assignment/XML escaping failed.'
}
foreach ($version in @('0.0.0', '1.2.3-preview.1', '1.2.3.4', '01.2.3', '65536.0.0', '1.2.3+build')) {
    Assert-Rejected { Set-MsixIdentity -Manifest (New-TestManifest) -Mode Test -ProductVersion $version }
}
Assert-Rejected { Set-MsixIdentity -Manifest (New-TestManifest) -Mode Store -ProductVersion '1.2.3' }
$example = Get-Content (Join-Path $root 'packaging/StoreIdentity.example.json') -Raw | ConvertFrom-Json
Assert-Rejected { Set-MsixIdentity -Manifest (New-TestManifest) -Mode Store -ProductVersion '1.2.3' -StoreIdentity $example }
$local = [pscustomobject]@{ Name = 'JTCStamper.LocalTest'; Publisher = 'CN=JTCStamper Local Test'; PublisherDisplayName = 'Local Test' }
Assert-Rejected { Set-MsixIdentity -Manifest (New-TestManifest) -Mode Store -ProductVersion '1.2.3' -StoreIdentity $local }
$manifest = New-TestManifest
if ($manifest.Package.Identity.ProcessorArchitecture -ne 'x64' -or
    $manifest.Package.Dependencies.TargetDeviceFamily.MinVersion -ne '10.0.22000.0' -or
    $manifest.Package.Applications.Application.Executable -ne 'JTCStamper.App.exe' -or
    $manifest.Package.Capabilities.Capability.Name -ne 'runFullTrust') { throw 'Unexpected package platform/capability.' }
[xml]$profile = Get-Content (Join-Path $root 'src/JTCStamper.App/Properties/PublishProfiles/Portable.pubxml') -Raw
if ($profile.Project.PropertyGroup.SelfContained -ne 'true' -or
    $profile.Project.PropertyGroup.PublishSingleFile -ne 'true' -or
    $profile.Project.PropertyGroup.RuntimeIdentifier -ne 'win-x64') { throw 'MSIX requires the self-contained Standard profile.' }
Write-Host 'MSIX identity, version, manifest and runtime-profile checks passed.'
