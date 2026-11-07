[CmdletBinding()]
param(
    [ValidateSet('Test', 'Store')][string]$Mode = 'Test',
    [string]$StoreIdentityFile = (Join-Path $PSScriptRoot '../packaging/StoreIdentity.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (![OperatingSystem]::IsWindows()) { throw 'Windows with the Windows 10/11 SDK is required.' }
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'MsixIdentity.ps1')
Push-Location $root
try {
    [xml]$properties = Get-Content Directory.Build.props -Raw
    [xml]$manifest = Get-Content packaging/AppxManifest.xml -Raw
    $identity = if ($Mode -eq 'Store') { Get-Content -LiteralPath $StoreIdentityFile -Raw | ConvertFrom-Json } else { $null }
    $manifest = Set-MsixIdentity -Manifest $manifest -Mode $Mode -ProductVersion $properties.Project.PropertyGroup.Version -StoreIdentity $identity
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $changes = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit source changes before packaging.' }
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $sdk = Get-ChildItem -LiteralPath $sdkBin -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path (Join-Path $_.FullName 'x64/makeappx.exe')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$sdk) { throw 'Install the Windows SDK with MakeAppx.exe.' }
    $makeappx = Join-Path $sdk.FullName 'x64/makeappx.exe'
    $output = Join-Path $root "dist/msix/$($Mode.ToLowerInvariant())"
    New-Item -ItemType Directory -Force $output | Out-Null
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('JTCStamper-Msix-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $stage | Out-Null
    try {
        & (Join-Path $PSScriptRoot 'Publish-App.ps1') -Variant Standard
        & (Join-Path $PSScriptRoot 'Update-ThirdPartyNotices.ps1') -Check
        $exe = Join-Path $root 'dist/win-x64/JTCStamper.App.exe'
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
        if ($info.ProductVersion -ne "$($properties.Project.PropertyGroup.Version)+$revision") {
            throw "Unexpected Standard EXE version: $($info.ProductVersion)"
        }
        # Explicit allowlist: never include the user's settings, journal or key.
        foreach ($file in @('dist/win-x64/JTCStamper.App.exe', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.txt')) {
            Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
        }
        Add-Type -AssemblyName System.Drawing
        $assetDirectory = Join-Path $stage 'Assets'
        New-Item -ItemType Directory $assetDirectory | Out-Null
        $source = [Drawing.Image]::FromFile((Join-Path $root 'src/JTCStamper.App/Assets/jtc-icon.png'))
        try {
            foreach ($asset in @(
                @{ Name = 'StoreLogo.png'; Size = 50 },
                @{ Name = 'Square44x44Logo.png'; Size = 44 },
                @{ Name = 'Square150x150Logo.png'; Size = 150 }
            )) {
                $bitmap = [Drawing.Bitmap]::new($asset.Size, $asset.Size)
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.DrawImage($source, 0, 0, $asset.Size, $asset.Size)
                    $bitmap.Save((Join-Path $assetDirectory $asset.Name), [Drawing.Imaging.ImageFormat]::Png)
                }
                finally { $graphics.Dispose(); $bitmap.Dispose() }
            }
        }
        finally { $source.Dispose() }
        $manifest.Save((Join-Path $stage 'AppxManifest.xml'))
        $package = Join-Path $output "JTCStamper-$($manifest.Package.Identity.Version)-$Mode-x64.msix"
        & $makeappx pack /d $stage /p $package /o
        if ($LASTEXITCODE -ne 0) { throw 'MakeAppx pack/manifest validation failed.' }
        & $makeappx unpack /p $package /d (Join-Path $stage 'unpacked') /o
        if ($LASTEXITCODE -ne 0) { throw 'MakeAppx unpack verification failed.' }
        $exeHash = (Get-FileHash (Join-Path $stage 'JTCStamper.App.exe') -Algorithm SHA256).Hash
        if ((Get-FileHash (Join-Path $stage 'unpacked/JTCStamper.App.exe') -Algorithm SHA256).Hash -ne $exeHash) {
            throw 'Packaged EXE differs from the Standard build.'
        }
        [ordered]@{
            Mode = $Mode; Identity = [string]$manifest.Package.Identity.Name
            Publisher = [string]$manifest.Package.Identity.Publisher
            Version = [string]$manifest.Package.Identity.Version; Revision = $revision
            WindowsSdk = $sdk.Name; SelfContained = $true; Architecture = 'x64'
            File = [IO.Path]::GetFileName($package)
            Sha256 = (Get-FileHash $package -Algorithm SHA256).Hash
            ExeSha256 = $exeHash; Signing = 'Unsigned'
            NotPerformed = @('Packaged activation', 'Install/update/uninstall', 'Office clipboard', 'Store submission')
        } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'build.json') -Encoding utf8
        Write-Host "Unsigned $Mode package: $package"
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force }
}
finally { Pop-Location }
