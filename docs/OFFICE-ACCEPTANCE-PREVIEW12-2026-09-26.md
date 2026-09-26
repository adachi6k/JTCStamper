# Office貼付経路の追加試験（preview.12）

製品は8436060/CI36208686077の通常版とLite。EXEハッシュは配布ZIP・先行受入と一致。製品の生成・照合実装は変更していない。今回のランナーは9a5862bに本変更を加えたビルド（各JSONにランナーEXEハッシュ）。Windows11 build26200、Office16.0.20326.20144、非管理者。合成画像・試験用文書のみで実施。

| 貼付経路 | 通常版 | Lite | 確認した内容 |
|---|---|---|---|
| Word 通常貼付 | 成功 | 成功 | 保存再読込、抽出PNGの画素/alpha/寸法保持、形比較 |
| Word DIB形式指定（DataType=5） | 失敗：指定形式を利用できない | 既存Wordプロセス保護で省略 | PNG通常貼付と区別。DIB指定は初版対応外 |
| Excel 通常貼付 | 成功 | 成功 | 保存再読込、抽出PNGの画素/alpha/寸法保持、形比較 |
| Excel PNG形式指定（Format=0） | 成功 | 成功 | 同上 |
| PowerPoint 通常貼付 | 成功 | 成功 | 同上 |
| PowerPoint PNG形式指定（DataType=6） | 成功 | 成功 | 同上 |

初回matrixは11成功・1失敗・5省略。失敗のWord DIB指定は例外「指定した種類のデータは使用できません」。以前の結果を合格へ上書きしない。製品のPNGコピーが成功していても、任意形式への変換が利用できるとは限らない。

所有者がExcelを閉じた後、Excel専用再試験は9成功・失敗/省略0（両版seed、生成履歴との元PNG照合、通常/PNG指定、元クリップボード復元）。ExcelのCOM参照に対応するPIDをブック操作前に調べ、開始前に存在したプロセスと異なることを要求。既存プロセスだった場合はスキップし、終了操作をしない。新規の専用インスタンスでも試験用ブックだけを閉じ、残るブックがない場合だけQuitする。

受入ランナーに--office-onlyと--office-excel-onlyを追加。製品EXEのオプションではない。通常の全面受入で通過済みの保存先・更新・復帰試験を不要に繰り返さず、Office経路を個別に再確認できる。初回の失敗・省略と再試験の成功は別JSONで保持。

生データ：test-results/office-matrix-preview12-2026-09-26.json、test-results/office-excel-preview12-2026-09-26.json。これはPNG保存画像の保持の検査であり、Office経由の全幾何コードの復号率を測り直したものではない。独立精度評価は変更しない。

APIの定義はMicrosoft公式資料で確認：[Excel Worksheet.PasteSpecial](https://learn.microsoft.com/en-us/office/vba/api/excel.worksheet.pastespecial)、[Word貼付形式](https://learn.microsoft.com/en-us/office/vba/api/word.wdpastedatatype)、[PowerPoint Shapes.Paste](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shapes.paste)、[Excel Application.Hwnd](https://learn.microsoft.com/en-us/office/vba/api/excel.application.hwnd)。実際の成否は本試験の観測結果。

試験結果を受け、通常の全面受入ではWordを確認済みの通常貼付に限定し、`--office-only`の互換性調査ではDIB指定を維持した。この経路選択の整理は初回ランナーとExcel再試験の後に行った。生データのランナーハッシュは実行時のものを保持する。
