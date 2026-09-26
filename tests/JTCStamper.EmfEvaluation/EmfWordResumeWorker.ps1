param([string]$App, [string]$StudyRoot, [string]$StudyOutput, [string]$InputDocument)
$ErrorActionPreference='Stop'
$report=[ordered]@{StartedUtc=[DateTime]::UtcNow.ToString('o');ClipboardReplaced=$false;InputDocument=$InputDocument;Word=[ordered]@{Status='Running';Samples=@()};Output=$StudyOutput}
function Write-Stage([string]$stage) {
    $report['LastStage']=$stage
    $report['LastStageUtc']=[DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $StudyOutput 'report.json')
    ($report.LastStageUtc+' '+$stage) | Add-Content -Encoding UTF8 (Join-Path $StudyOutput 'stages.log')
    Write-Host $stage
}
$document=$null
try {
    Write-Stage 'Word resume: checking saved input'
    $originalHash=(Get-FileHash -LiteralPath $InputDocument -Algorithm SHA256).Hash
    $report['InputSha256']=$originalHash
    # Operate on a copy; never save or change the user's original document.
    $copy=Join-Path $StudyOutput 'saved-input.docx'
    Copy-Item -LiteralPath $InputDocument -Destination $copy -ErrorAction Stop
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $originalHash) { throw 'Input copy hash mismatch.' }
    Write-Stage 'Word resume: creating application'
    $word=New-Object -ComObject Word.Application
    $word.Visible=$true
    Write-Stage 'Word resume: opening saved document read-only'
    $document=$word.Documents.Open($copy,$false,$true,$false)
    $report.Word['ReopenedShapes']=$document.InlineShapes.Count
    if ($document.InlineShapes.Count -ne 6) { throw 'Expected six inline EMF images in the saved test document.' }
    for ($i=1;$i -le $document.InlineShapes.Count;$i++) {
        $shape=$document.InlineShapes.Item($i)
        $report.Word.Samples += [ordered]@{Index=$i;WidthMm=([double]$shape.Width*25.4/72);HeightMm=([double]$shape.Height*25.4/72)}
    }
    $pdf=Join-Path $StudyOutput 'word-resumed.pdf'
    Write-Stage 'Word resume: exporting PDF (no SaveAs2)'
    $document.ExportAsFixedFormat($pdf,17)
    if (-not (Test-Path -LiteralPath $pdf) -or (Get-Item -LiteralPath $pdf).Length -eq 0) { throw 'PDF was not created.' }
    $report.Word['PdfSha256']=(Get-FileHash -LiteralPath $pdf -Algorithm SHA256).Hash
    Write-Stage 'Word resume: closing read-only copy'
    $document.Close(0)
    $document=$null
    if ((Get-FileHash -LiteralPath $InputDocument -Algorithm SHA256).Hash -ne $originalHash) { throw 'Original input hash changed during test.' }
    $report.Word.Status='Completed'
    Write-Host 'Saved Word document reopened and exported to PDF. Original unchanged; clipboard untouched.'
} catch {
    $report.Word.Status='Failed';$report.Word['Error']=$_.ToString()
    Write-Host "FAILED: $_"
} finally {
    if ($null -ne $document) {
        Write-Stage 'Word resume: closing test copy after failure'
        try { $document.Close(0) } catch { $report.Word['CleanupError']=$_.ToString() }
    }
    $report['FinishedUtc']=[DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $StudyOutput 'report.json')
}
if ($report.Word.Status -ne 'Completed') { exit 1 }
exit 0
