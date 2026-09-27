# #17：EMF出力の検討と試験計画

2026-09-27。対象：[Issue #17](https://github.com/adachi6k/JTCStamper/issues/17)。利用者の了承によりEMFの製品採用を見送り、PNGに一本化する。Issueは採用見送りでクローズする。以下の検討・試験記録は保持するが、未完了の受入条件を達成した扱いにはしない。

## 最終判断：採用見送り

15mm中心の実用途で、今回の限定比較ではPNGに対するEMFの明確な優位性を確認できなかった。半透明・インクむらの外観再現、Excel→PDFの寸法差、Word自動保存・PDF出力の待機問題が残る。生成履歴統合・保存失敗/GDIリソース検証・大規模な誤対応評価まで追加する負担に対し、現時点の便益が十分ではないため製品機能には追加しない。

PowerPoint、Word（手動PDF）、Excelで基本的な経路を確認。各PDFの150/300dpiでは2方式それぞれ4/4復号、プレーンは各0/2。ただし2コードのみで一般的な精度保証ではない。試験コードと成果物は研究記録として残す。

再検討は、拡大印刷でPNG解像度が不足する、実際のOffice→PDF経路でPNGの劣化や復号失敗が問題になるなど、具体的な利用要求が出た場合に行う。同条件のPNGに対する改善と外観・寸法の互換性を確認して採否を決める。以下の「未実施」「後続」等の記述は検討時点の記録であり、現在の継続開発予定ではない。

## 方針

StampRenderer.CreateDrawingが返す共通のDrawingGroupからPNGとEMFを作る。文字は現在と同じYu Gothicの輪郭を利用し、円・横線はストロークを輪郭へ展開する。試験用EMFは輪郭を折れ線に近似しGDIのパスで塗りつぶす。描画データをPNG化して包む方式ではない。外円（線幅込み87.1設計単位）の直径15mmに対応するEMFフレームを指定する。実際のOffice配置寸法は検証待ち。

## 半透明とかすれ

- classic EMFのAlphaBlendはビットマップを参照する。EMFであること自体はベクトルであることを保証しない。
- EMF+は候補だが、Office形式指定貼付・保存再読込・PDF変換での対応は未評価。Dualの代替GDI記録に画像が入っていないかも調べる必要がある。
- 試験用Crispは純粋な朱色と完全な切れ目。透明度マスクを省き、半透明の切れ目は描かない。背景部分は描画しない。
- WhitePaperApproximationは白背景に対する平均90%のインク量と10%の切れ目を不透明な薄色に近似する。インクむらの空間分布は再現しない。背景全体を白で塗る方式ではないが、有色背景では半透明PNGと一致しない。
- この2方式は実験条件であり、利用者の同意なく製品出力の見た目を切り替えるものではない。PNGの外観は維持する。
- 本採用前に、(a)ベクトル優先の別出力プロファイル、(b)EMF+、(c)輪郭はベクトル・効果だけ画像の混在、をOffice結果と照合精度で比較する。混在の場合は「全体がベクトル」と表示しない。

## 試験用コード

experiments/JTCStamper.EmfEvaluation。共通のStampRendererをリンクし、通常/プレーン×上段文字6パターン×2外観＝24 EMFと参考PNGを出力する。空文字、長い文字、異体字を含む。クリップボードも生成履歴も操作しない。

EMFレコードの境界、ヘッダー・EOF・FillPathを検査し、ビットマップ転送・AlphaBlend・GDIコメントを検出したら失敗させる。これは実行時チェックであり、ビルド成功だけで「画像レコード0を確認済み」としない。

CreateEnhMetaFile / CloseEnhMetaFile / DeleteEnhMetaFile、ブラシの選択復元・DeleteObjectをfinallyで実施。将来の所有権検証として、反復生成時のGetGuiResources、API各段階の失敗注入、クリップボード移譲前後のリーク検査を追加する。今の試作はファイル出力だけなのでクリップボード所有権の検証は未完了。

## 製品へ組み込む前の保存設計

IClipboard.Copy(byte[] png)だけではEMFと生成イベントを結び付けられない。次のCopyPayload/生成成果物モデルへ拡張する案とする（未実装）。

1. 一つのイベントID・Stamp・描画プロファイルからPNGとEMFを生成。出力に失敗したらクリップボードに触れない。
2. Generatedに用途メモ・実生成時刻・表示日付、Renderer/AppearanceVersion、PNGとEMFの各SHA-256、物理寸法、出力形式一覧をHMAC保護して保存。EMF本体も保存し、再生成だけに依存しない。
3. 履歴のプレビューと比較用PNGは、選択したEMF外観を再生した画像を含め、テクスチャPNGとは別の成果物として対応させる。複数形式を同時提供するときに外観が違うまま同一画像とみなさない。
4. CopyRequestedを保存してからクリップボードへ。利用者がPNG/EMFを明示選択できる案を先行し、Officeが自動選択する複数形式同時提供は後で評価。
5. CF_ENHMETAFILEはHENHMETAFILEを渡す。成功後はOS所有なのでDeleteEnhMetaFileしない。失敗・未移譲分はアプリが解放。所有者HWNDを使い、OpenClipboard→EmptyClipboard→SetClipboardData→CloseClipboardの失敗と部分成功を区別する。
6. CopyCompletedは提供形式を記録する。保存失敗時に成功を表示しない。OSへの移譲済みでも貼付完了とは記録しない。複数形式の一部だけ渡せた場合も要求全体の成功にしない。

## 残る受入試験

- PNG共通描画への抽出前後の画素一致。実際のWPFで通常/プレーン・日付境界・文字境界を確認。
- EMFの再生画像・15mm寸法・穴のある文字・輪郭/色・白/有色背景を比較。
- Word/Excel/PowerPointの形式指定貼付、保存・閉じる・再読込、15mm配置、標準/最小PDF書出し。PDF処理は研究用で製品へのPDF取込みと分離。
- 同一コードのPNG/EMFを別集計。正規画像の復号率、誤コード復号率、対照画像からの復号、履歴への誤対応率を区別。文字だけの一致を真正性としない。
- 生成/要求/完了の各保存失敗、クリップボード競合、GDIリソース反復検査。
- 出力形式選択・利用者向けヘルプ・CIのWindows専用試験を整備してから本体機能を有効化。

## 今回の検証範囲

アプリと試験用プロジェクトのReleaseビルド成功。試験用プロジェクトはネットワーク不通に伴うNuGet監査取得失敗のため、ローカルキャッシュでNuGetAudit=falseを指定してビルドした（設定ファイルは変更せず、脆弱性監査を実施したとは扱わない）。Windows起動はWSL UtilBindVsockAnyPort socket failedで失敗。EMF生成、レコード検査、再生、Office試験は未実行。

## 一次資料

- [EMF作成と物理寸法](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createenhmetafilew)
- [Alpha blendingはアルファ付きビットマップ](https://learn.microsoft.com/en-us/windows/win32/gdi/alpha-blending)
- [EMRALPHABLEND](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-emralphablend)
- [EMF+とDual](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/metafiles-in-gdi)
- [SetClipboardDataの所有権](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata)

## クリップボード試験の追加

試験コマンド`--copy-emf FILE`を追加。非表示HWNDを所有者としてCF_ENHMETAFILEを設定し、直後の読み戻しバイト列を元EMFと比較する。成功移譲後にハンドルを解放せず、未移譲ハンドルだけfinallyで解放。生成履歴とはまだ接続しない。

Invoke-EmfClipboardOffice.ps1は試験成果物のStudyRootを指定して実行。新規PowerPointプレゼンに形式指定貼付し、ネイティブ寸法を記録、保存・再読込・PDF化する。クリップボードは置き換える。既存のプレゼンを閉じず、PowerPoint本体も終了しない。試験用ビルド成功、Windows実行は未確認。PNG列はファイル挿入の参照であり、PNGクリップボード経路の回帰試験ではない。

## PowerPointクリップボード実機結果（2026-09-27）

利用者PCの実行記録clipboard-20260927-014017-ae6e4691を確認。6件すべてのCF_ENHMETAFILE読み戻しが一致。貼付キャンバス16.54mm（目標16.5327mm）、再読込後PDFの外円約14.990mm。150/300dpiの復号はCrispとWhitePaperApproximation各4/4、プレーン誤復号各0/2。2コードだけの限定試験であり、漏洩/失敗注入や一般的な誤受入率の合格とはしない。

後続のInvoke-EmfWordExcel.ps1を追加。WordはRange.PasteSpecial(DataType=9)、ExcelはWorksheet.PasteSpecial("Picture (Enhanced Metafile)")と日本語名のフォールバックを使用。各アプリの新規文書のみ操作し、保存再読込後PDF化する。現在Windows実行未確認。

## Word待機問題と監視の分離

最初のWord/Excel連続試験は6件分のEMFコピー後に停止。Wordの最終貼付～保存付近と推定するが、正確な停止APIは旧ログでは不明。Ctrl+CとWordウィンドウ閉鎖でも復帰せず、利用者が試験用PowerShellのみ停止。Word/Excelの試験合格とは扱わない。

旧Invoke-EmfWordExcel.ps1は無効化。Invoke-EmfOfficeSafe.ps1 -App Word/Excelが監視用プロセスとなり、COM呼び出しはEmfOfficeWorker.ps1の子PowerShellだけで実行する。既定は処理進行なし30秒・全体180秒。タイムアウト時は自分で起動した子プロセスだけKillし、Office本体やプロセスツリーは停止しない。所有プロセスIDと最終段階をsupervision.jsonへ残す。段階ログはCOM呼び出し前に書く。旧Word/Excel連続実行・停止後の自動再試行は行わない。

Office操作の前に、待機する試験用子PowerShellを起動して2秒で停止する監視の自己試験を行う。自己試験に失敗した場合はOfficeへ進まない。-SelfTestOnlyも提供。監視用スクリプト自体はWindows実行待ちであり、停止を実機確認済みとはしない。中断後はOffice内に試験文書が残る場合がある。文書を自動強制破棄しない。

## 保存済みWord文書からの再開

利用者が終了時に保存した「JTC EMF clipboard test.docx」（OneDrive/ドキュメント、2026-09-27 02:23:58更新）を確認。試験ラベルと6件のEMFを含みZIP整合性も正常。自動保存の停止原因は未特定だが、手動保存済み成果物を利用して後続を分離できる。

監視スクリプトの-InputDocumentでEmfWordResumeWorker.ps1へ分岐。入力を試験フォルダに複製し、読み取り専用で開き、6画像の寸法を記録してPDF出力する。SaveAs2も再貼付も行わない。元文書のハッシュを前後比較し、クリップボードは操作しない。Office実行は利用者環境での確認待ち。

## Excel実機結果（2026-09-27）

safe-excel-20260927-023434-eb602fdeのワーカーはCompleted、XLSX/PDFあり。6件すべて形式指定貼付・保存・再読込を完了し、XLSX内の6 EMFは元データとSHA256一致。監視側はExitCode=nullのためFailedとしており、Office失敗とは区別する。取得時にプロセスハンドルを保持する修正を追加したがWindows上の再検証は未実施。元のログは上書きしない。

PDFは画像リソース0・ベクトル描画138件。外円15.3468×15.3852mmで15mmより約2.3～2.6%大きい。ネイティブ貼付キャンバス16.5806mmとは別にPDF経路の寸法差があり、原因未特定、寸法要件の完全合格とはしない。150/300dpiで、既知の印影周辺切り出しに対し製品の自動検出/復号を実行、Crisp・WhitePaperApproximation各4/4、誤コード0、プレーン誤復号各0/2。2コードだけの限定評価。
