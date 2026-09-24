[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
# Run after the Standard publish: its runtimeconfig records the versions actually bundled.
$configPath = Join-Path $root 'src/JTCStamper.App/bin/Release/net10.0-windows/win-x64/JTCStamper.App.runtimeconfig.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$frameworks = $config.runtimeOptions.includedFrameworks
$assets = Get-Content (Join-Path $root 'src/JTCStamper.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$folders = @($assets.packageFolders.PSObject.Properties.Name)
$header = @'
JTC Stamper third-party notices

The Standard distribution contains .NET and Windows Desktop runtime components.
The Lite distribution requires a separately installed Microsoft .NET Desktop Runtime.
The following notices are reproduced from the Microsoft runtime packages used for builds.
Installed Windows fonts are referenced for rendering; font files are not redistributed.
The JTC icon was generated for this project during development; no external icon font is bundled.
'@
$sections = @($header.Trim())
foreach ($item in @(
    @{ Framework = 'Microsoft.NETCore.App'; Package = 'microsoft.netcore.app.runtime.win-x64'; Files = @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') },
    @{ Framework = 'Microsoft.WindowsDesktop.App'; Package = 'microsoft.windowsdesktop.app.runtime.win-x64'; Files = @('LICENSE') }
)) {
    $version = @($frameworks | Where-Object name -eq $item.Framework).version
    if (-not $version -or $version -notmatch '^\d+\.\d+\.\d+$') { throw 'Cannot identify bundled runtime version.' }
    foreach ($file in $item.Files) {
        $relative = "$($item.Package)/$version/$file"
        $paths = @($folders | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ })
        if ($paths.Count -ne 1) { throw "Cannot identify runtime notice: $relative" }
        $body = (Get-Content -LiteralPath $paths[0] -Raw).Replace("`r`n", "`n").Trim()
        $sections += "===== $($item.Package) / $version / $file =====`n`n$body"
    }
}
$expected = ($sections -join "`n`n`n") + "`n"
$target = Join-Path $root 'THIRD-PARTY-NOTICES.txt'
if ($Check) {
    $actual = (Get-Content -LiteralPath $target -Raw).Replace("`r`n", "`n")
    if ($actual -cne $expected) { throw 'Runtime notices differ from the bundled dependencies. Run Update-ThirdPartyNotices.ps1 after Standard publish and review the change.' }
} else {
    [IO.File]::WriteAllText($target, $expected, [Text.UTF8Encoding]::new($false))
}
