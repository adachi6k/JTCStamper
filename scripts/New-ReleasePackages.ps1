[CmdletBinding()]
param([string]$SigningCertificateThumbprint)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $changes = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit source changes before packaging.' }
    [xml]$properties = Get-Content Directory.Build.props -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    . (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
    if (!(Test-ReleaseVersion $version)) { throw 'Version must be SemVer MAJOR.MINOR.PATCH[-prerelease], without build metadata (the SDK adds the Git revision).' }
    $tag = git tag --points-at HEAD
    if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Tag and product version disagree.' }
    $output = Join-Path $root 'dist/packages'
    New-Item -ItemType Directory -Force $output | Out-Null
    $packages = @()
    foreach ($variant in @(
        @{ Name = 'Standard'; Profile = 'Portable'; Directory = 'win-x64' },
        @{ Name = 'Lite'; Profile = 'Lite'; Directory = 'lite-win-x64' }
    )) {
        $publish = Join-Path $root ('dist/' + $variant.Directory)
        dotnet publish src/JTCStamper.App -c Release "-p:PublishProfile=$($variant.Profile)" "-p:SourceRevisionId=$revision" -o $publish
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($variant.Name)" }
        if ($variant.Name -eq 'Standard') { & (Join-Path $PSScriptRoot 'Update-ThirdPartyNotices.ps1') -Check }
        $exe = Join-Path $publish 'JTCStamper.App.exe'
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
        if ($info.ProductVersion -ne "$version+$revision") { throw "Unexpected EXE version: $($info.ProductVersion)" }
        $signing = $null
        if ($SigningCertificateThumbprint) {
            $signing = & (Join-Path $PSScriptRoot 'Sign-ReleaseExecutable.ps1') -Path $exe -CertificateThumbprint $SigningCertificateThumbprint
        }
        $stage = Join-Path ([IO.Path]::GetTempPath()) ('JTCStamper-Package-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory $stage | Out-Null
        try {
            # Explicit allowlist: never package a user's journal, key or settings.
            Copy-Item -LiteralPath $exe -Destination $stage
            $guide = Get-Content docs/user/DISTRIBUTION.md -Raw
            if ($signing) {
                $guide = "This EXE is self-signed. Windows does not trust this certificate by default; SmartScreen warnings may remain. No timestamp. Signer: $($signing.Thumbprint)`r`n`r`n" + $guide
            }
            [IO.File]::WriteAllText((Join-Path $stage 'README.txt'), "$($variant.Name) / $version`r`n`r`n$guide", [Text.UTF8Encoding]::new($true))
            foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $root $notice) -Destination $stage }
            $metadata = [ordered]@{
                Version = $version; Revision = $revision; Tags = @($tag)
                Variant = $variant.Name; Runtime = 'win-x64'; Signing = $signing
                ExeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash
                ExeBytes = (Get-Item $exe).Length
            }
            $metadata | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage 'build.json') -Encoding utf8
            $zip = Join-Path $output "JTCStamper-$version-$($variant.Name)-win-x64.zip"
            Compress-Archive -LiteralPath @((Join-Path $stage 'JTCStamper.App.exe'), (Join-Path $stage 'README.txt'), (Join-Path $stage 'build.json'), (Join-Path $stage 'LICENSE'), (Join-Path $stage 'NOTICE'), (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')) -DestinationPath $zip -Force
            $packages += [pscustomobject]@{ File = [IO.Path]::GetFileName($zip); Sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash; Build = $metadata }
        }
        finally { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
    $packages | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'checksums.json') -Encoding utf8
    Write-Host "Packages: $output"
}
finally { Pop-Location }
