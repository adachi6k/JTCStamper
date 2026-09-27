param(
    [Parameter(Mandatory=$true)][ValidateSet('Word','Excel')][string]$App,
    [string]$StudyRoot,
    [string]$InputDocument,
    [ValidateRange(10,120)][int]$StageTimeoutSeconds=30,
    [ValidateRange(30,600)][int]$TotalTimeoutSeconds=180,
    [switch]$SelfTestOnly
)
$ErrorActionPreference='Stop'
# Resolve after parameter binding, not in a parameter default expression.
if ([string]::IsNullOrWhiteSpace($StudyRoot)) {
    $scriptFile = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptFile)) {
        throw 'Cannot resolve the script directory. Specify -StudyRoot explicitly.'
    }
    $StudyRoot = [IO.Path]::GetDirectoryName($scriptFile)
}
$StudyRoot = (Resolve-Path -LiteralPath $StudyRoot -ErrorAction Stop).ProviderPath
if ([string]::IsNullOrWhiteSpace($StudyRoot)) { throw 'StudyRoot did not resolve to a filesystem directory.' }
$studyOutput=Join-Path $StudyRoot ('safe-'+$App.ToLower()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $studyOutput | Out-Null
$supervision=[ordered]@{Status='Starting';App=$App;StageTimeoutSeconds=$StageTimeoutSeconds;TotalTimeoutSeconds=$TotalTimeoutSeconds;Output=$studyOutput}
$child=$null
$testChild=$null
$code=1
function Start-Isolated([string]$command,[string]$name) {
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $ownedProcess = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-NonInteractive','-STA','-ExecutionPolicy','RemoteSigned','-EncodedCommand',$encoded) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $studyOutput ($name+'.stdout.log')) -RedirectStandardError (Join-Path $studyOutput ($name+'.stderr.log'))
    # Retain the process handle while alive so Windows PowerShell can retrieve ExitCode.
    $null = $ownedProcess.Handle
    return $ownedProcess
}
function Stop-OwnedChild($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        # Kill this exact child only. No process-name matching and no process-tree kill.
        $process.Kill()
        if (-not $process.WaitForExit(5000)) { throw "Owned child PID $($process.Id) did not exit." }
    }
}
try {
    # Verify the watchdog with a sleeping child before touching Office or the clipboard.
    $testChild=Start-Isolated 'Start-Sleep -Seconds 120' 'watchdog-test'
    if ($testChild.WaitForExit(2000)) { throw 'Watchdog preflight child exited unexpectedly.' }
    Stop-OwnedChild $testChild
    $supervision['WatchdogSelfTest']='Passed'
    Write-Host 'Watchdog preflight passed (only its own test process was stopped).'
    if ($SelfTestOnly) {
        $supervision.Status='SelfTestPassed';$code=0
    } else {
        $worker=Join-Path $StudyRoot 'EmfOfficeWorker.ps1'
        if (-not [string]::IsNullOrWhiteSpace($InputDocument)) {
            if ($App -ne 'Word') { throw 'InputDocument is supported for Word only.' }
            $InputDocument=(Resolve-Path -LiteralPath $InputDocument -ErrorAction Stop).ProviderPath
            if ([IO.Path]::GetExtension($InputDocument) -ne '.docx') { throw 'Expected a DOCX test document.' }
            $worker=Join-Path $StudyRoot 'EmfWordResumeWorker.ps1'
            $supervision['InputDocument']=$InputDocument
        }
        if (-not (Test-Path -LiteralPath $worker)) { throw 'Worker script not found.' }
        function Quote-Literal([string]$s) { return "'"+$s.Replace("'","''")+"'" }
        $command='& '+(Quote-Literal $worker)+' -App '+(Quote-Literal $App)+' -StudyRoot '+(Quote-Literal $StudyRoot)+' -StudyOutput '+(Quote-Literal $studyOutput)
        if (-not [string]::IsNullOrWhiteSpace($InputDocument)) { $command += ' -InputDocument '+(Quote-Literal $InputDocument) }
        $command += '; exit $LASTEXITCODE'
        $child=Start-Isolated $command 'worker'
        $supervision['WorkerPid']=$child.Id
        $supervision.Status='Running'
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $lastProgress=0.0
        $lastToken=''
        $lastStage='Worker startup'
        Write-Host "$App test started. Each stage is limited to $StageTimeoutSeconds seconds."
        if ([string]::IsNullOrWhiteSpace($InputDocument)) { Write-Host 'Clipboard contents will be replaced.' }
        else { Write-Host 'Read-only saved-document test: clipboard is not changed.' }
        while (-not $child.WaitForExit(250)) {
            $path=Join-Path $studyOutput 'report.json'
            if (Test-Path -LiteralPath $path) {
                try {
                    $state=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
                    if ($state.LastStageUtc -and $state.LastStageUtc -ne $lastToken) {
                        $lastToken=$state.LastStageUtc;$lastProgress=$timer.Elapsed.TotalSeconds;$lastStage=$state.LastStage
                        Write-Host $lastStage
                    }
                } catch { } # A short partial write is retried on the next poll.
            }
            if (($timer.Elapsed.TotalSeconds-$lastProgress) -ge $StageTimeoutSeconds -or $timer.Elapsed.TotalSeconds -ge $TotalTimeoutSeconds) {
                $supervision.Status='TimedOut';$supervision['LastStage']=$lastStage
                Stop-OwnedChild $child
                Write-Host "TIMEOUT at: $lastStage"
                Write-Host 'Only the test PowerShell was stopped. Word/Excel were NOT terminated. Do not rerun yet.'
                break
            }
        }
        if ($supervision.Status -eq 'Running') {
            $child.WaitForExit()
            $supervision['WorkerExitCode']=$child.ExitCode
            if ($null -eq $child.ExitCode) { throw 'Worker exit code unavailable; inspect report.json and artifacts. This is a supervisor error, not an Office failure.' }
            if ($child.ExitCode -eq 0) {
                $state=Get-Content -LiteralPath (Join-Path $studyOutput 'report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($state.$App.Status -ne 'Completed') { throw 'Worker exited without a completed report.' }
                $supervision.Status='Completed';$code=0
                Write-Host "$App completed."
            } else {
                $supervision.Status='Failed'
                Get-Content -LiteralPath (Join-Path $studyOutput 'worker.stdout.log') -Tail 12
                Get-Content -LiteralPath (Join-Path $studyOutput 'worker.stderr.log') -Tail 12
            }
        }
    }
} catch {
    $supervision.Status='Failed';$supervision['Error']=$_.ToString()
    Write-Host "FAILED: $_"
} finally {
    try { Stop-OwnedChild $child } catch { Write-Host "Worker cleanup: $_" }
    try { Stop-OwnedChild $testChild } catch { Write-Host "Preflight cleanup: $_" }
    $supervision['FinishedUtc']=[DateTime]::UtcNow.ToString('o')
    $supervision | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $studyOutput 'supervision.json')
    Write-Host "Results: $studyOutput"
}
exit $code
