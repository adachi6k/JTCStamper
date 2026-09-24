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
4. Windows DPAPIで鍵を保存・復号し、生成・コピー状態・注釈・HMACを再読込で確認。この通常の記録試験では偽のクリップボードを注入。
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

## GitHub Actionsへの移行

`docs/ci/windows-smoke.yml`に手動実行用の雛形を用意しています。まだ`.github/workflows`へ配置していないため、自動実行やGitHubへの送信はありません。

移行時は雛形を`.github/workflows/windows-smoke.yml`へコピーします。Windows runnerに.NET 10 SDKを導入し、Core検査→2版publish→同じPowerShellバッチ→結果アップロードの順に実行します。`contents: read`のみを許可し、失敗時もレポートを保存します。

既定のCIではクリップボード試験を明示的に省略します。実際に利用するrunnerの対話セッションで動作を確認してから、必要なら専用Windows runnerで`-IncludeClipboard`を有効にしてください。対話デスクトップがない環境でWPF表示自体に失敗した場合も、成功や未実施へ読み替えず失敗として残します。導入時にActionのバージョン・固定SHA・組織ポリシーを確認してください。

## この環境での検証状況

C#のWindows向けビルド、通常版・軽量版のpublish、Coreテストを実施。Windows実機テストとPowerShellスクリプトの実行は未実施です。WSLからWindowsプロセスを起動する接続が`UtilBindVsockAnyPort: socket failed`で失敗するためです。生成済みレポートを装った成功結果は用意していません。

## 12-bitの追加評価

ring12-<phase>.jsonに、WPFによる72条件（2氏名・6値・2サイズ・PNG/JPEG80/JPEG80+4度）と対照を記録し、結果フォルダーへコピーします。Windows評価は未実施です。実施済みの独立Pillow合成画像評価はRING12-CODE.mdを参照してください。

タブ化後のスモークテストには、3タブの存在、照合コントロールの再利用、入力内容の保持、メインウィンドウ内での表示を確認する `main-tabs-preserve-view-and-inputs` を追加しています。この変更後のWindows実行は未確認です。

## 外観・テーマの追加確認

左サイドバー、ライト／ダーク／Windows設定の切り替え、appearance.jsonへの保存・再読込、照合画面の同一インスタンス維持、印影プレビューPNGの不変性をテストします。window-Light-<phase>.png、window-Dark-<phase>.png、window-System-<phase>.pngを出力します。これらはWindows用テストであり、Linuxでのビルド成功は実機試験の成功を意味しません。

統合タイトルバーの追加検査では、通常表示と最大化時のWM_NCHITTESTを実際のウィンドウへ送り、空白領域がHTCAPTION、メニューがHTCLIENT、最大化ボタンがHTMAXBUTTONとなることを確認します。3つの操作ボタンは寸法とベクター形状に加え、描画画素も検査します。スナップのポップアップ・実際のドラッグ・複数DPIモニター間の移動はこの自動検査には含めません。

## GitHub Actions

`.github/workflows/windows-smoke.yml`をmainへのpush、Pull Request、手動起動で実行します。
SDKはglobal.json、ActionsはコミットSHAで固定します。Coreチェック、両版のpublish、6プロセスのWindowsスモークを行い、失敗時も許可リスト内のレポートとテーマ別画像を保存します。

ホストランナーの試験は実ユーザーの受入試験を代替しません。Office、実クリップボード、ランタイム未導入PC、DPI・Narratorの対話操作は未実施として実機試験Issueで追跡します。初回ActionsでGUIの実行条件を満たさない場合も、成功扱いにせず結果を記録します。
