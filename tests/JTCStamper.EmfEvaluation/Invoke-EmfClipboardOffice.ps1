param([string]$StudyRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$studyRoot = $StudyRoot
$studyOutput = Join-Path $studyRoot ('clipboard-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $studyOutput | Out-Null
$runner = Join-Path $studyRoot 'runner\JTCStamper.EmfEvaluation.exe'
$source = Join-Path $studyRoot 'normalized'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Status='Running'; Method='CF_ENHMETAFILE / PowerPoint PasteSpecial(2)'; ClipboardReplaced=$true; Samples=@(); Output=$studyOutput }
$ownedPresentation = $null
$reopened = $null
try {
    $powerPoint = New-Object -ComObject PowerPoint.Application
    $powerPoint.Visible = -1
    $ownedPresentation = $powerPoint.Presentations.Add()
    $ownedPresentation.PageSetup.SlideWidth = 720
    $ownedPresentation.PageSetup.SlideHeight = 405.36
    $specimens = @('1-coded','4-coded','1-plain')
    foreach ($stem in $specimens) {
        $slide = $ownedPresentation.Slides.Add($ownedPresentation.Slides.Count + 1, 12)
        $title = $slide.Shapes.AddTextbox(1, 20, 12, 680, 35)
        $title.TextFrame.TextRange.Text = "$stem - Clipboard EMF roundtrip"
        $title.TextFrame.TextRange.Font.Size = 22
        $x = 90
        foreach ($profile in @('Crisp','WhitePaperApproximation')) {
            $file = Join-Path $source ($stem + '-' + $profile + '.emf')
            $copyOutput = & $runner '--copy-emf' $file 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Clipboard command failed: $copyOutput" }
            $copyOutput | Add-Content -Encoding UTF8 (Join-Path $studyOutput 'clipboard-readback.log')
            $shapeRange = $slide.Shapes.PasteSpecial(2)
            $shape = $shapeRange.Item(1)
            $nativeWidthMm = [double]$shape.Width * 25.4 / 72
            $nativeHeightMm = [double]$shape.Height * 25.4 / 72
            $shape.Left = $x
            $shape.Top = 120
            # Do not resize: native paste dimensions are part of the experiment.
            $label = $slide.Shapes.AddTextbox(1, $x-30, 65, 210, 45)
            $label.TextFrame.TextRange.Text = "$profile`nNative size (no resizing)"
            $label.TextFrame.TextRange.Font.Size = 14
            $report.Samples += [ordered]@{ Stem=$stem; Profile=$profile; NativeWidthMm=$nativeWidthMm; NativeHeightMm=$nativeHeightMm; ExpectedCanvasMm=(1500*96/87.1/100); Sha256=(Get-FileHash -Algorithm SHA256 $file).Hash; ClipboardReadback="$copyOutput" }
            $x += 220
        }
        $reference = Join-Path $source ($stem + '-reference.png')
        $png = $slide.Shapes.AddPicture($reference,0,-1,530,120,(1500*96/87.1/100*72/25.4),(1500*96/87.1/100*72/25.4))
        $label = $slide.Shapes.AddTextbox(1, 510, 65, 180, 45)
        $label.TextFrame.TextRange.Text = 'PNG file reference'
        $label.TextFrame.TextRange.Font.Size = 14
    }
    $pptx = Join-Path $studyOutput 'clipboard-office.pptx'
    $ownedPresentation.SaveAs($pptx,24)
    $ownedPresentation.Close()
    $ownedPresentation = $null
    $reopened = $powerPoint.Presentations.Open($pptx,-1,0,0)
    $reopened.SaveAs((Join-Path $studyOutput 'clipboard-office.pdf'),32)
    $reopened.Close()
    $reopened = $null
    $report.Status = 'Completed'
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    Write-Host 'Clipboard readback, PasteSpecial, save/reopen, and PDF export completed.'
} catch {
    $report.Status = 'Failed'
    $report.Error = $_.ToString()
    Write-Host "FAILED: $_"
} finally {
    if ($null -ne $reopened) { try { $reopened.Close() } catch {} }
    if ($null -ne $ownedPresentation) { try { $ownedPresentation.Saved = -1; $ownedPresentation.Close() } catch {} }
    # Do not Quit PowerPoint: the user may have other presentations open.
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $studyOutput 'report.json')
    Write-Host "Results: $studyOutput"
}
if ($report.Status -ne 'Completed') { exit 1 }
