# preview.11追加受入試験（2026-09-26）

製品ソース40dfe09f82123c4485b0269c86a796beee4a5549、main CI 36161518390の配布候補。リリースタグはまだない。Windows 11 build 26200、非管理者、Office16.0.20326.20144。通常版はインストール済み.NET10なし、軽量版は専用のDesktop Runtime10.0.12を使用。

- Standard SHA-256: 9E55B6DCB4148EFCD7675438682FF9DF453047859982F83227C28BC44C4E5314
- Lite SHA-256: CEF781380768CC43742B48BDB0E1C61F18A284FD3A8330967914ECD5E5173368

全体試験22件は19成功・2失敗・1省略。失敗は通常版と軽量版の260文字超EXEパスからの起動。履歴処理に入る前のCreateProcess失敗であり、成功へ読み替えない。この環境はLongPathsEnabled=0。製品は短いEXEパスからの起動を案内し、この条件の長いEXEパスは対応範囲外とした。

EXEパスと保存先を分けた追加5件はすべて成功。短いEXEパスから、427文字（Standard）／423文字（Lite）のjournalパスで初回生成・注釈・保存・再起動確認が成功。OS設定は変更していない。これは長いEXEパスが起動できる証明ではない。

実クリップボードを別の所有テストプロセスで占有した際はCopyFailedとなり、CopyCompletedなし。占有解除後に成功。元のクリップボードを復元した。一般アプリの強制終了や既存ユーザー文書の変更は行っていない。

自己展開先を通常ファイルで塞いだケースは、ホストがエラーを返し鍵・履歴を作らず、障害除去後に起動成功。実空き容量ゼロの試験とは別である。

preview.10→preview.11更新、EXEロックによる置換失敗で元バイトを保持、preview.10への復帰を両版で確認。同じスキーマの試験であり、将来のスキーマ変更や新版だけのデータの互換性は保証しない。

通常版→軽量版と軽量版→通常版の差替え、起動・生成・コピー・注釈・再起動・照合が成功。通常版のWord/Excel/PowerPoint、軽量版のWord/PowerPointは貼付→保存→再オープン→画像抽出で透明画素と寸法を維持（Word/ExcelはPaste、PowerPointはPasteSpecial(6)によるPNG形式指定）。軽量版のExcelは既存プロセスを検出したため省略（所有者のアプリと区別できないプロセスを閉じない）。preview.10では両版3製品が成功しているが、最新EXEの省略をその結果で置き換えない。

証跡はtest-results/acceptance-preview11-extended-2026-09-26.jsonおよびacceptance-preview11-long-data-2026-09-26.json。追加試験のランナーはf5e4d95の内容（ビルド直前の変更を後からコミットしたため埋込版番号は34269e4）。製品EXEはどちらの試験も同じハッシュ。

未実施: ペイント／形式指定貼付、実スリープ復帰、OneDriveオンラインのみ／同期競合、実ブラウザー経由の警告。GUIの確認済み範囲はGUI-ACCEPTANCE-PREVIEW11-2026-09-26.mdに分離する。

## 実際の空き容量ゼロでの自己展開

追加ソース34269e4、CI36165776900の全ジョブが成功。専用256MiB NTFS VHDの実空き容量ゼロで、通常版の初回自己展開が管理コード開始前に失敗し、空き容量を戻した後には管理コードの引数検証へ到達することを確認。満杯化のWin32エラー112、空き0、自己展開ホストの終了コードと復旧終了コードをbundle-full-disk-full-2026-09-26.jsonへ保存。8件の容量不足試験すべて成功、VHD切断・削除も確認。既存PCのディスクは満杯にしていない。試験に使った34269e4ビルドのEXEハッシュも同JSONに記録（上記40dfe09とは異なる成果物）。

## タグ版での最終追加試験

GitHubはPrivate、ReleaseはDraftのままです。

- タグ v0.1.0-preview.11 / acad6e0372fe0cb09091b60c297c8f147417ddf1
- [タグCI](https://github.com/adachi6k/JTCStamper/actions/runs/36166583336): 全ジョブ成功。Core47、Windows156成功・実クリップボード6はCI省略。実NTFS容量不足8件、保存中断、別Windowsマシン間の移行も成功。
- 実Windows11 build26200、非管理者、Office16.0.20326.20144: 20成功・2省略・失敗0。両版のExcel貼付は既存プロセス保護のため省略。Word/PPT、実コピー・競合失敗・再起動・注釈・照合・版交換・旧版復帰が成功。
- 通常版は.NET10のない環境、Liteは専用Desktop Runtime10.0.12で試験。Liteのランタイムなし診断も確認。
- 400文字超の保存先は成功。260文字超のEXEパスは別試験で失敗したため非対応。短い場所へ展開してください。
- タグ・ZIP・EXE・CI・容量不足レポート・実機試験のハッシュを照合。ZIPはEXE/README/build.json/LICENSE/NOTICE/THIRD-PARTY-NOTICESの6ファイルのみ。

Standard EXE SHA-256: 4A7C3F16AC1A43E4BD8BF3B20D9EE8F37C7CBEA973533E25FBA67C937A143AEA

Lite EXE SHA-256: D5C275616359A0E442D12AF6806075F3C723F81C63513E05078E4C13DF7E178E

[検証用Releaseドラフト](https://github.com/adachi6k/JTCStamper/releases)に同じZIPとchecksums.jsonを配置しました。閲覧には所有者のGitHubログインが必要です。

残り: 実ブラウザーからのダウンロード警告と起動、Excel/Paintや形式指定貼付、GUI細目・実スリープ・OneDrive条件、照合説明の理解度と最終配布判断。EMFは初回PNGプレビューに未搭載です。Privateを維持します。

独立画像評価では原寸のコード復号15/16で95%目標未達、小画像で誤最上位もありました。文字・日付とコードによる候補検索の試験機能として扱い、真正性の証明や無断コピーの判定には使いません。

ユーザーの既存アプリ、鍵・履歴は差し替えていません。EXEを試す場合はZIPを専用フォルダーに展開してください。

## Office貼付経路の厳密な範囲

受入ランナーはWord/ExcelのPasteと、PowerPointのPasteSpecial(6)（PNG形式指定）を使用する。過去報告にまとめて「通常貼付」とした表現があっても、PowerPointの通常貼付を実証したとは扱わない。Word/Excelの形式指定、PowerPointの通常貼付、Paintは別の未実施条件として#5に残す。
