param([ValidateSet('Word','Excel')][string]$App, [string]$StudyRoot, [string]$StudyOutput)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $StudyRoot 'runner\JTCStamper.EmfEvaluation.exe'
$source = Join-Path $StudyRoot 'normalized'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); ClipboardReplaced=$true; Word=[ordered]@{Status='NotRun';Samples=@()}; Excel=[ordered]@{Status='NotRun';Samples=@()}; Output=$studyOutput }
function Write-Stage([string]$stage) {
    $report['LastStage'] = $stage
    $report['LastStageUtc'] = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $studyOutput 'report.json')
    ($report.LastStageUtc + ' ' + $stage) | Add-Content -Encoding UTF8 (Join-Path $studyOutput 'stages.log')
    Write-Host $stage
}
$fixtures = @()
foreach ($stem in @('1-coded','4-coded','1-plain')) {
    foreach ($profile in @('Crisp','WhitePaperApproximation')) {
        $fixtures += [pscustomobject]@{Stem=$stem;Profile=$profile;File=(Join-Path $source "$stem-$profile.emf")}
    }
}
function Copy-TestEmf($fixture) {
    Write-Stage "$App`: clipboard $($fixture.Stem) $($fixture.Profile)"
    $message = & $runner '--copy-emf' $fixture.File 2>&1
    if ($LASTEXITCODE -ne 0) { throw "EMF clipboard failed: $message" }
    $message | Add-Content -Encoding UTF8 (Join-Path $studyOutput 'clipboard-readback.log')
}
function Measure-Shape($fixture,$shape,$method) {
    return [ordered]@{ Stem=$fixture.Stem; Profile=$fixture.Profile; Method=$method; WidthMm=([double]$shape.Width*25.4/72); HeightMm=([double]$shape.Height*25.4/72); ExpectedCanvasMm=(1500*96/87.1/100); Sha256=(Get-FileHash -Algorithm SHA256 $fixture.File).Hash }
}
if ($App -eq 'Word') {
$document = $null
$reopened = $null
try {
    Write-Stage 'Word: creating application'
    $word = New-Object -ComObject Word.Application
    $word.Visible = $true
    Write-Stage 'Word: creating document'
    $document = $word.Documents.Add()
    $document.PageSetup.PageWidth = 595.28
    $document.PageSetup.PageHeight = 841.89
    $document.PageSetup.TopMargin = 36
    $document.PageSetup.BottomMargin = 36
    $document.Content.Font.Size = 11
    $document.Content.ParagraphFormat.SpaceAfter = 6
    $document.Content.ParagraphFormat.LineSpacingRule = 0
    $document.Content.Text = "JTC EMF clipboard test - Word`r"
    foreach ($fixture in $fixtures) {
        Write-Stage "Word: text/range $($fixture.Stem) $($fixture.Profile)"
        $tail = $document.Range($document.Content.End-1,$document.Content.End-1)
        $tail.InsertAfter("$($fixture.Stem) / $($fixture.Profile)`r")
        $target = $document.Range($document.Content.End-1,$document.Content.End-1)
        Copy-TestEmf $fixture
        $before = $document.InlineShapes.Count
        # Range.PasteSpecial(IconIndex, Link, Placement, DisplayAsIcon, DataType, IconFileName, IconLabel)
        Write-Stage "Word: paste $($fixture.Stem) $($fixture.Profile)"
        $target.PasteSpecial([Type]::Missing,$false,0,$false,9,[Type]::Missing,[Type]::Missing)
        Write-Stage "Word: pasted $($fixture.Stem) $($fixture.Profile)"
        if ($document.InlineShapes.Count -ne $before+1) { throw 'Word did not add exactly one inline EMF.' }
        $shape = $document.InlineShapes.Item($document.InlineShapes.Count)
        $report.Word.Samples += Measure-Shape $fixture $shape 'Range.PasteSpecial(DataType=9)'
        Write-Stage "Word: text/range $($fixture.Stem) $($fixture.Profile)"
        $tail = $document.Range($document.Content.End-1,$document.Content.End-1)
        $tail.InsertAfter("`r")
    }
    $docx = Join-Path $studyOutput 'word-clipboard.docx'
    Write-Stage 'Word: SaveAs2 DOCX'
    $document.SaveAs2($docx,16)
    Write-Stage 'Word: SaveAs2 finished'
    Write-Stage 'Word: close saved document'
    $document.Close(0)
    $document = $null
    Write-Stage 'Word: reopen DOCX'
    $reopened = $word.Documents.Open($docx,$false,$true)
    $report.Word.ReopenedShapes = $reopened.InlineShapes.Count
    if ($reopened.InlineShapes.Count -ne $fixtures.Count) { throw 'Word image count changed after reopen.' }
    Write-Stage 'Word: export PDF'
    $reopened.ExportAsFixedFormat((Join-Path $studyOutput 'word-clipboard.pdf'),17)
    Write-Stage 'Word: close reopened document'
    $reopened.Close(0)
    $reopened = $null
    $report.Word.Status = 'Completed'
    Write-Host 'Word: clipboard paste, save/reopen, PDF completed.'
} catch {
    $report.Word.Status = 'Failed'; $report.Word.Error = $_.ToString()
    Write-Host "Word FAILED: $_"
} finally {
    if ($null -ne $reopened) { try { $reopened.Close(0) } catch {} }
    if ($null -ne $document) { try { $document.Close(0) } catch {} }
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $studyOutput 'report.json')
}
}
if ($App -eq 'Excel') {
$workbook = $null
$reopenedBook = $null
try {
    Write-Stage 'Excel: creating application'
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $true
    $workbook = $excel.Workbooks.Add(-4167)
    $sheet = $workbook.Worksheets.Item(1)
    $sheet.Name = 'EMF test'
    $sheet.Range('A1:H40').RowHeight = 15
    $sheet.Range('A1:H40').ColumnWidth = 9
    $sheet.Range('A1').Value2 = 'JTC EMF clipboard test - Excel'
    $row = 3
    foreach ($fixture in $fixtures) {
        $sheet.Cells.Item($row,1).Value2 = "$($fixture.Stem) / $($fixture.Profile)"
        $sheet.Activate()
        $anchor = $sheet.Cells.Item($row+1,2)
        $anchor.Select() | Out-Null
        Copy-TestEmf $fixture
        $before = $sheet.Shapes.Count
        # Microsoft's documented Excel format name; try its Japanese label if unavailable.
        $format = 'Picture (Enhanced Metafile)'
        Write-Stage "Excel: paste $($fixture.Stem) $($fixture.Profile)"
        try { $sheet.PasteSpecial($format,$false,$false) }
        catch {
            if ($sheet.Shapes.Count -ne $before) { throw }
            $format = '図 (拡張メタファイル)'
            $sheet.PasteSpecial($format,$false,$false)
        }
        if ($sheet.Shapes.Count -ne $before+1) { throw 'Excel did not add exactly one EMF.' }
        $shape = $sheet.Shapes.Item($sheet.Shapes.Count)
        $shape.Left = $anchor.Left
        $shape.Top = $anchor.Top
        # Preserve native width and height; prevent cell geometry from resizing the picture.
        $shape.Placement = 3
        $report.Excel.Samples += Measure-Shape $fixture $shape "Worksheet.PasteSpecial($format)"
        $row += 6
    }
    $sheet.PageSetup.PrintArea = '$A$1:$H$40'
    $sheet.PageSetup.Zoom = 100
    $sheet.PageSetup.PaperSize = 9
    $sheet.PageSetup.Orientation = 1
    $xlsx = Join-Path $studyOutput 'excel-clipboard.xlsx'
    Write-Stage 'Excel: save XLSX'
    $workbook.SaveAs($xlsx,51)
    Write-Stage 'Excel: close saved workbook'
    $workbook.Close($false)
    $workbook = $null
    Write-Stage 'Excel: reopen XLSX'
    $reopenedBook = $excel.Workbooks.Open($xlsx,0,$true)
    $report.Excel.ReopenedShapes = $reopenedBook.Worksheets.Item(1).Shapes.Count
    if ($report.Excel.ReopenedShapes -ne $fixtures.Count) { throw 'Excel image count changed after reopen.' }
    Write-Stage 'Excel: export PDF'
    $reopenedBook.ExportAsFixedFormat(0,(Join-Path $studyOutput 'excel-clipboard.pdf'),0)
    Write-Stage 'Excel: close reopened workbook'
    $reopenedBook.Close($false)
    $reopenedBook = $null
    $report.Excel.Status = 'Completed'
    Write-Host 'Excel: clipboard paste, save/reopen, PDF completed.'
} catch {
    $report.Excel.Status = 'Failed'; $report.Excel.Error = $_.ToString()
    Write-Host "Excel FAILED: $_"
} finally {
    if ($null -ne $reopenedBook) { try { $reopenedBook.Close($false) } catch {} }
    if ($null -ne $workbook) { try { $workbook.Close($false) } catch {} }
    # Do not Quit either application or alter global security/alert settings.
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $studyOutput 'report.json')
    Write-Host "Results: $studyOutput"
}
}
if ($report[$App].Status -ne 'Completed') { exit 1 }
