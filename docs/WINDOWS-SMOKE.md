# Windows実機スモークテスト

通常版・軽量版の実際の配布EXEを、個人データから分離して起動します。Officeと任意画像の照合は対象外です。

## 実行方法

Windows x64、.NET 10 Desktop Runtime x64、ログイン中のデスクトップで実行します。ビルドも行う場合は.NET 10 SDKが必要です。

```powershell
# リポジトリのルートで実行
.\publish-all.cmd
powershell.exe -NoProfile -File .\scripts\Invoke-WindowsSmoke.ps1

# クリップボードも検査する場合（現在の内容をテスト印影で上書きします）
powershell.exe -NoProfile -File .\scripts\Invoke-WindowsSmoke.ps1 -IncludeClipboard
```

PowerShell 7の`pwsh -File`でも同じスクリプトを使えます。会社のスクリプト実行ポリシーは変更しません。実行が禁止されている環境では管理方針に従ってください。

出力は`test-results/windows-smoke/<実行ID>/`。集約結果は`summary.json`、各プロセスの検査結果は`report-seed.json`または`report-verify.json`。ウィンドウと印影のPNGも保存します。スクリーン全体を撮影せず、テスト用ウィンドウの描画内容だけを書き出します。

成功は終了コード0、検査失敗・タイムアウト・レポート欠損は1です。アプリ側のテスト引数不正は2、レポート書込み失敗は3としてバッチ側で失敗扱いになります。クリップボードを指定しない場合は該当検査がskippedとなり、全機能を検証済みとは扱いません。

## 検査内容

1. 通常版を専用TEMPフォルダーへコピーし、実際のMainWindowの表示・ContentRendered・初期値・プレビュー・PNGアイコンを確認。当日モードでは編集不可であること、指定ボタンと当日へ戻す操作も確認。
2. PNGのサイズ、透明余白、2桁年、日付変更を確認。さらに、印影を96pxで文書内に配置し、0度/4度の回転・JPEG品質85で赤い印影の検出と履歴候補の取得を確認（実Officeやスキャン精度の保証ではありません）。
3. 設定ファイルの保存と再読込、不正入力で元ファイルを変更しないことを確認（保存ダイアログのUI操作は対象外）。
4. Windows DPAPIで鍵を保存・復号し、生成・コピー状態・注釈・HMACを再読込で確認。クリップボード未指定時はダミー、指定時は実クリップボードを使い、生成履歴に保存されたPNGを読戻し対象にします。
5. 別のテスト用履歴で書込み失敗・改変を再現し、コピーを呼ばないこととHMACの拒否を確認。
6. オプション指定時は本物のクリップボードへPNGとBitmapを登録し、PNGバイト列と画像寸法を読戻し確認。
7. プロセスを終了して同じEXEを再起動し、鍵・設定・注釈を含む履歴のSHA-256と内容を確認。クリップボード指定時はプロセス終了後のPNG保持も確認。
8. 同じテスト用配置先でEXEだけを軽量版に交換し、同じデータの読込みと一致を確認。
9. 軽量版から始める逆方向でも同じ一式を実行。

正常時は計6プロセス（通常版seed/restart/replacement、軽量版seed/restart/replacement）のレポートが得られます。seed失敗時には依存する試験を実行せず、全体を失敗扱いにします。

この交換試験は今回の通常版↔軽量版の互換性です。過去バージョンからのスキーマ移行の保証ではありません。別PC・別ユーザーへのDPAPI移行、.NET未導入環境の案内、実際のOffice貼付、文字の見た目の良否は対象外です。

## データの分離

`%TEMP%/JTCStamper-Smoke-<GUID>/`に専用EXEと合成データを作成します。アプリは`--smoke-test --test-root ... --phase seed|verify`でのみテストモードに入り、TEMP内の専用マーカーと未使用状態を検証します。不正引数では通常起動へフォールバックしません。実際の保存先・保存済み設定を参照しません。

テスト用鍵・履歴はTEMPに残し、結果フォルダーにはJSONレポートと合成PNGだけをコピーします。GitHub Actionsへアップロードする対象もこの結果フォルダーだけです。プロセスがタイムアウトした場合はバッチが起動したプロセスのみ終了します。

## この環境での検証状況

GitHub ActionsでCoreとWindowsスモークを実行済み。実機では通常版のランタイム非依存起動、軽量版のランタイム不足診断と隔離ランタイム使用、実クリップボード、Office COM経由の貼付・保存・再読込も確認中です。成功・失敗・未実施の詳細はIssue #5に記録します。

実クリップボードの連続読戻しで、WPF/OLE経由だけが先頭から壊れたPNGを返す現象を再現しました。同じクリップボード更新番号のまま、Windows API経由では元のPNGとバイト単位で一致することを確認しています。PNGの読取はOpenClipboardとGlobalLockで保護したメモリをコピーしてから解放する経路へ変更しました。HMACやPNG完全一致の条件は緩めていません。CIでは実クリップボードを明示的に省略し、実機結果はIssue #5に記録します。

## 12-bitの追加評価

ring12-<phase>.jsonに、WPFによる72条件（2氏名・6値・2サイズ・PNG/JPEG80/JPEG80+4度）と対照を記録し、結果フォルダーへコピーします。Windows評価はCIのring12レポートを参照してください。実施済みの独立Pillow合成画像評価はRING12-CODE.mdを参照してください。

タブ化後のスモークテストには、3タブの存在、照合コントロールの再利用、入力内容の保持、メインウィンドウ内での表示を確認する `main-tabs-preserve-view-and-inputs` を追加しています。CIとローカル実機で実行します。

## 外観・テーマの追加確認

左サイドバー、ライト／ダーク／Windows設定の切り替え、appearance.jsonへの保存・再読込、照合画面の同一インスタンス維持、印影プレビューPNGの不変性をテストします。window-Light-<phase>.png、window-Dark-<phase>.png、window-System-<phase>.pngを出力します。これらはWindows用テストであり、Linuxでのビルド成功は実機試験の成功を意味しません。

統合タイトルバーの追加検査では、通常表示と最大化時のWM_NCHITTESTを実際のウィンドウへ送り、空白領域がHTCAPTION、メニューがHTCLIENT、最大化ボタンがHTMAXBUTTONとなることを確認します。3つの操作ボタンは寸法とベクター形状に加え、描画画素も検査します。スナップのポップアップ・実際のドラッグ・複数DPIモニター間の移動はこの自動検査には含めません。

## GitHub Actions

`.github/workflows/windows-smoke.yml`をmainへのpush、Pull Request、手動起動で実行します。
SDKはglobal.json、ActionsはコミットSHAで固定します。Coreチェック、両版のpublish、6プロセスのWindowsスモークを行い、失敗時も許可リスト内のレポートとテーマ別画像を保存します。

ホストランナーの試験は実ユーザーの受入試験を代替しません。Office、実クリップボード、ランタイム未導入PC、DPI・Narratorの対話操作は未実施として実機試験Issueで追跡します。初回ActionsでGUIの実行条件を満たさない場合も、成功扱いにせず結果を記録します。

## バックアップと未保存入力の追加検査

seedで同じユーザー用バックアップを作成・復元し、DPAPI鍵、署名付き記録、画像、注釈、原本認証を比較します。既存復元先と破損バックアップを拒否することも検証します。restart/replacementでは復元済み履歴も再読込します。バックアップや復元データはTEMP内だけに置き、CI成果物の許可リストへ追加しません。

別イベント間の注釈下書きの保持、編集した印面設定と元に戻した設定の状態も実ウィンドウオブジェクトで検査します。確認ダイアログの操作性やユーザーによるファイル選択は自動試験の対象外です。

## Officeを含むローカル受入ランナー

`tests/JTCStamper.WindowsAcceptance`は独立したWindows用コンソールEXEです。PowerShellの実行ポリシーを変更せず、アプリ内のスモークとOfficeの文書APIを呼び出します。管理者権限は不要です。

ビルドは `dotnet publish tests/JTCStamper.WindowsAcceptance -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`。

専用ルートに`.acceptance-root`マーカー、`Standard/JTCStamper.App.exe`、`Lite/JTCStamper.App.exe`、`runtime/`（テストする.NET 10 Desktop Runtimeの隔離配置）を用意し、EXEへそのルートを1引数で渡します。既存データのフォルダーを渡さないでください。runtime配置はOSへのインストールではなく、テストプロセスだけにDOTNET_ROOTを指定します。

最初にクリップボードを安全に複製できるか確認し、未対応形式ならクリップボードとOfficeテストを省略します。複製できた場合だけ合成画像をコピーし、最後に元へ戻します。Officeでは新規の合成文書だけを作り、保存名は毎回異なるものを使います。起動済みのWord／Excelがあればその製品は省略し、既存PowerPointを終了しません。認証・セキュリティ画面の操作は行いません。

acceptance.jsonと各seed/restart/replacement.jsonに結果・OS・EXEハッシュを記録します。Office保存ファイルも合成画像のみですが、TEMP以下の鍵・履歴・バックアップを成果物へ含めないでください。失敗時は終了コード1、マーカー不備は2です。skippedを成功として扱わないでください。

このランナーはOfficeのUIによる通常操作、ペイント、DPI/Narrator、スリープ復帰、SmartScreenの検証を代替しません。

## PNG読取の回帰検証

通常の配布EXEで、seedごとに8回の実コピーとPNG全体の一致を検証し、別プロセスと配布版交換後にも保持を確認します。データオブジェクトが返された後もPNGストリームを読めることは、グローバルなクリップボードを使わないCI検査にも含めます。

NativeClipboardは最大32MiBを確認してから、クリップボードを開いた状態でGlobalLock→Marshal.Copy→GlobalUnlockを行い、最後にCloseClipboardします。Windowsが所有するハンドルは解放しません。

- [GetClipboardDataの所有権と使用条件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata)
- [GlobalLockとGlobalUnlock](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globallock)

Office受入ランナーもPNGの退避と検査には同じ読取コードを利用し、退避中にクリップボードが変わった場合はテストを省略します。--skip-officeは原因調査時だけOfficeを明示的に省略するオプションです。結果ファイルは試験開始時に既知の6レポート名だけを消去し、過去の結果が混ざらないようにします。

通常版と軽量版それぞれの生成PNGについて、履歴との完全一致を確認してからWord・Excel・PowerPointへ貼付し、保存・再読込後のPNGバイト列、透明画素を含む全画素、表示寸法を確認します。テスト配置先は日本語と空白を含む専用パスです。試験用文書は毎回別名で、既存文書を上書きしません。

## 起動・コピーの非同期処理

初回描画とは別にInitializationPendingを待ってから履歴とコントロールを検査します。起動中の終了要求、初期保存先の失敗、コピー中の二重実行・保存先変更・タブ切替・終了要求を実ウィンドウで検査します。保存先の競合によってコピー前とコピー後の記録確定を失敗させ、成功表示しないことと、終了を保留したエラー表示を確認します。これらは実ディスク枯渇や電源断を再現したという意味ではありません。

seedの生成は実際のMainWindow.CopyAsyncを通します。CIはクリップボードだけをダミーにし、ローカル受入ではWindowsClipboardを使用します。描画とクリップボードはSTA、履歴I/Oは背景処理という同じ経路です。再起動・相互版交換とOffice検査は、その履歴に記録されたPNGを対象にします。


## 保存中断・全削除の回帰検証

`tests/JTCStamper.StorageFailures`は自分が起動した専用子プロセスだけをチェックポイントで強制終了します。TEMP内の専用フォルダーに`.storage-failure-root`（内容`JTCStamper isolated storage interruption data v1`）を置き、`dotnet run --project tests/JTCStamper.StorageFailures -c Release -- --run <専用フォルダー>`で実行します。既存の鍵・履歴は使用せず、再解析ポイントは拒否します。子プロセスは親が消失した場合も60秒で終了します。

検査点はDPAPI保護済み鍵の部分書込、生成前、Generated確定後、ダミーコピー先の処理中、CopyCompleted確定後です。鍵の部分書込はKeyStoreと共通のAtomicFileヘルパーを直接通し、確定名key.dpapiが現れないことと、再起動時の鍵作成・再読込を確認します。他の点では実際のJournal/CopyServiceを使用し、完了前の記録を成功と扱わないこと、完了済み記録の保持、HMACと画像情報の検証、既存記録を変えず再生成できることを確認します。クリップボードは試験用の受け口であり、通常のWindowsクリップボードを変更しません。

App内のスモークでは、実際のMainWindowの削除処理へ取消・確定を渡します。取消時の全記録保持、ファイルロックによる途中失敗と画面更新、再試行、空表示、鍵と別保存原本の保持、削除後の再生成を検査します。確認ダイアログそのものの手動操作・読み上げを検査したという意味ではありません。通常画面の確認は既定値Cancelのままです。

GitHub Actionsにも組み込み、公開対象は`storage-failures.json`だけです。中断後の一時ファイル・DPAPI鍵・履歴はアップロードしません。物理的なディスク容量不足、電源断、OneDrive同期競合、実クリップボード処理中の強制終了は別条件として未検証です。


容量不足の再現手順・対象・安全上の制限は[DISK-FULL.md](DISK-FULL.md)に記載しています。実行結果は対応する検証レポートで確認し、例外注入と実VHDの結果を分けて扱います。
