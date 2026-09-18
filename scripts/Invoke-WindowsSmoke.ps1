[CmdletBinding()]
param(
    [string]$StandardExe = (Join-Path $PSScriptRoot '..\dist\win-x64\JTCStamper.App.exe'),
    [string]$LiteExe = (Join-Path $PSScriptRoot '..\dist\lite-win-x64\JTCStamper.App.exe'),
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '..\test-results\windows-smoke'),
    [switch]$IncludeClipboard,
    [ValidateRange(15, 600)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows is required.' }
$standard = (Resolve-Path -LiteralPath $StandardExe).Path
$lite = (Resolve-Path -LiteralPath $LiteExe).Path
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory)) $runId
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('JTCStamper-Smoke-' + $runId)
New-Item -ItemType Directory -Path $results, $workspace | Out-Null
$runs = New-Object 'System.Collections.Generic.List[object]'

function Invoke-Phase([string]$Exe, [string]$DataRoot, [string]$Phase, [string]$Label) {
    $arguments = '--smoke-test --test-root "' + $DataRoot + '" --phase ' + $Phase
    if ($IncludeClipboard) { $arguments += ' --clipboard' }
    $reportPath = Join-Path $DataRoot ('report-' + $Phase + '.json')
    # Only remove previous generated reports; all journal/settings/key files remain untouched.
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
    $ringReport = Join-Path $DataRoot ('ring12-' + $Phase + '.json')
    if (Test-Path -LiteralPath $ringReport) { Remove-Item -LiteralPath $ringReport }
    $geometryReport = Join-Path $DataRoot ('geometry-' + $Phase + '.json')
    if (Test-Path -LiteralPath $geometryReport) { Remove-Item -LiteralPath $geometryReport }
    $process = $null
    $passed = $false
    $detail = ''
    $exitCode = $null
    $skipped = @()
    try {
        $process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
        $null = $process.Handle # Keep a process handle so ExitCode remains available after exit.
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Timeout after $TimeoutSeconds seconds."
        }
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        if (-not (Test-Path -LiteralPath $reportPath)) { throw "No report was produced (exit $exitCode). Check the .NET Desktop Runtime prerequisite for Lite." }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.SchemaVersion -ne 1 -or $report.Phase -ne $Phase) { throw 'Unexpected report format/phase.' }
        $failed = @($report.Checks | Where-Object { $_.Status -eq 'failed' })
        $skipped = @($report.Checks | Where-Object { $_.Status -eq 'skipped' } | ForEach-Object { $_.Name })
        $passed = ($exitCode -eq 0 -and $report.Passed -eq $true -and $failed.Count -eq 0 -and @($report.Checks).Count -gt 0)
        $detail = if ($passed) { 'Requested checks passed. See skipped checks separately.' } else { 'Application reported a failure.' }
    }
    catch { $detail = $_.Exception.Message }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $artifact = Join-Path $results $Label
        New-Item -ItemType Directory -Path $artifact -Force | Out-Null
        # Allowlist artifacts. Never publish a key, journal, baseline, or arbitrary test directory.
        foreach ($file in @(('report-' + $Phase + '.json'), ('window-' + $Phase + '.png'), ('stamp-' + $Phase + '.png'), ('geometry-' + $Phase + '.json'), ('ring12-' + $Phase + '.json'))) {
            $source = Join-Path $DataRoot $file
            if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $artifact }
        }
        $runs.Add([pscustomobject]@{ Name = $Label; Passed = $passed; ExitCode = $exitCode; Detail = $detail; Skipped = $skipped })
        Write-Host "$Label : $passed ($detail)"
    }
    return $passed
}

if ($IncludeClipboard) { Write-Warning 'The clipboard test replaces the current clipboard with a synthetic stamp. Close apps that compete for the clipboard.' }
try {
    foreach ($case in @(
        @{ Name = 'standard'; First = $standard; Replacement = $lite },
        @{ Name = 'lite'; First = $lite; Replacement = $standard }
    )) {
        $folder = Join-Path $workspace $case.Name
        $dataRoot = Join-Path $folder 'data'
        New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $dataRoot '.jtc-smoke-root'), 'JTCStamper isolated smoke data v1')
        $exe = Join-Path $folder 'JTCStamper.App.exe'
        Copy-Item -LiteralPath $case.First -Destination $exe
        $seedPassed = Invoke-Phase $exe $dataRoot 'seed' ($case.Name + '-seed')
        if ($seedPassed) {
            $null = Invoke-Phase $exe $dataRoot 'verify' ($case.Name + '-restart')
            # Replace only the EXE in this disposable folder, leaving its data exactly as written.
            Copy-Item -LiteralPath $case.Replacement -Destination $exe -Force
            $null = Invoke-Phase $exe $dataRoot 'verify' ($case.Name + '-replacement')
        }
        else {
            $runs.Add([pscustomobject]@{ Name = $case.Name + '-dependent-checks'; Passed = $false; ExitCode = $null; Detail = 'Not run: seed phase failed.'; Skipped = @('restart', 'replacement') })
        }
    }
}
catch {
    $runs.Add([pscustomobject]@{ Name = 'runner'; Passed = $false; ExitCode = $null; Detail = $_.Exception.ToString(); Skipped = @() })
}
$failedRuns = @($runs | Where-Object { -not $_.Passed })
$summary = [pscustomobject]@{
    SchemaVersion = 1
    RunId = $runId
    Passed = ($failedRuns.Count -eq 0 -and $runs.Count -eq 6)
    ClipboardRequested = [bool]$IncludeClipboard
    StandardSha256 = (Get-FileHash -LiteralPath $standard -Algorithm SHA256).Hash
    LiteSha256 = (Get-FileHash -LiteralPath $lite -Algorithm SHA256).Hash
    Runs = @($runs.ToArray())
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding UTF8
# Leave synthetic data in TEMP for troubleshooting; only allowlisted reports are in the results directory.
Write-Host "Reports: $results"
Write-Host "Disposable test data: $workspace"
if (-not $summary.Passed) { exit 1 }
exit 0
