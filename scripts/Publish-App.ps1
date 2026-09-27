[CmdletBinding()]
param([ValidateSet('All', 'Standard', 'Lite')][string]$Variant = 'All')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    foreach ($build in @(
        @{ Name = 'Standard'; Profile = 'Portable'; Directory = 'win-x64' },
        @{ Name = 'Lite'; Profile = 'Lite'; Directory = 'lite-win-x64' }
    )) {
        if ($Variant -ne 'All' -and $Variant -ne $build.Name) { continue }
        $output = Join-Path $root ('dist/' + $build.Directory)
        dotnet publish src/JTCStamper.App/JTCStamper.App.csproj -c Release "-p:PublishProfile=$($build.Profile)" -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($build.Name)" }
        Write-Host "$($build.Name): $output/JTCStamper.App.exe"
    }
}
finally { Pop-Location }
