[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$valid = @('0.1.0-preview.12', '0.1.0', '1.0.0', '1.2.3-rc.1', '1.2.3-alpha-beta.1', '1.2.3-0', '1.2.3-001a', '1.2.3--', '10.20.30')
$invalid = @('', '1.2', '1.2.3.4', '01.2.3', '1.02.3', '1.2.03', '1.2.3-preview.01', '1.2.3-', '1.2.3-a..b', '1.2.3-a.', 'v1.2.3', ' 1.2.3', "1.2.3`n", '1.2.3_alpha', '1.2.3+build.1')
foreach ($value in $valid) {
    if (!(Test-ReleaseVersion $value)) { throw "Valid product version rejected: $value" }
}
foreach ($value in $invalid) {
    if (Test-ReleaseVersion $value) { throw "Invalid product version accepted: $value" }
}
Write-Host "$($valid.Count + $invalid.Count) product-version checks passed."
