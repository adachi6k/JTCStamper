# 容量不足の試験

通常の保存先やホストのディスクを満杯にしません。scripts/Invoke-DiskFull.ps1は、TEMP内の毎回異なるフォルダーに固定容量256 MiBのVHDを新規作成し、そのファイルから取得したディスクだけを初期化・NTFSフォーマットします。既存のVHDやディスク番号を入力として受け取りません。ホストには開始時1 GiB以上の空き容量が必要です。DiskPart用コマンドファイルの文字コードを固定するため、TEMPはASCII文字のパスを使用します。

管理者権限のあるWindowsと.NET 10 SDKで、リポジトリから ./scripts/Invoke-DiskFull.ps1 を実行します。管理者権限がなければblockedを記録して終了します。実行ポリシーを変更・迂回しません。GitHub Actionsの専用Windows環境でも同じスクリプトを実行します。

ランナーは別ドライブ、NTFS、32〜256 MiB、毎回変わるラベルとマーカーを検証してから書き込みます。ホストのシステムドライブ、再解析ポイントを経由した書込先、別ボリュームに保存できないレポートは拒否します。書込量・ファイル数・時間にも上限を設けています。通常終了と捕捉できた失敗時は専用VHDを切り離して削除し、cleanup.jsonへ結果を記録します。

## 試験内容

Coreの自動テストは、Generated／CopyRequested／CopyCompletedの各書込直前でWindowsの容量不足に相当する例外（39／112）を注入します。各失敗でのコピー呼出数、完了記録の不在、再実行、既存記録の保持を検査します。例外注入を実ディスクの試験とは数えません。

VHD試験は実際にデータを書いてクラスタを消費します。NTFSでは小さなデータを既存のMFTレコードへ格納できるため、空き容量の表示だけでは失敗を保証できません。対象ディレクトリへ実処理と同じ形式の一時ファイルを作成し、容量不足による失敗が続く状態にしてから本体の処理を実行します。**成功条件は本体の書込から実際に39／112の容量不足エラーが返ること**です。別の例外や、書き込めたケースを成功として扱いません。

1. 初回鍵保存の失敗で、不完全なkey.dpapiを確定しない。容量回復後に作成・再読込できる。
2. Generated、CopyRequested、CopyCompletedの各確定前に満杯にする。CopyServiceからJournal／AtomicFileを実際に呼び出し、残る記録が0／1／2件、コピー呼出が0／0／1回であることを検査する。
3. 原本とバックアップの上書き失敗で既存ファイルの全バイトが変化しない。回復後に保存し、バックアップを新しいフォルダーへ復元して認証する。
4. MainWindow.CopyAsyncのコピー前・コピー後に満杯にする。エラー表示、成功と扱わないこと、容量回復後の再コピー、履歴の認証を確認する。

IJournalWriterはコピー処理の追記境界です。通常のアプリは従来どおりJournalを渡します。VHD試験用のラッパーは指定の境界でディスクを満杯にした後、実際のJournal.Appendを呼び出します。容量不足例外を偽装しません。

クリップボードは試験用の受け口であり、ユーザーのクリップボードを変更しません。原本検査はGUIのファイル選択ダイアログではなく、同じ署名済み記録とAtomicFileによる保存経路を通します。バックアップは実際のHistoryBackup.Save/Restore、画面試験は実際のMainWindowを使います。

test-results/disk-fullのレポートだけをCI成果物にします。VHD、DPAPI鍵、生成履歴はアップロードしません。容量不足・後片付けに失敗した場合はジョブを失敗にします。

## 検証範囲

WindowsのNTFSが容量不足を返す条件の検査です。物理ディスクの故障、電源断、OneDrive同期競合、OS側TEMPの枯渇、実クリップボードAPI内の異常は別条件です。

参考：[Windowsの容量不足エラー](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-)、
[NTFSのMFTと容量](https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/ntfs-reserves-space-for-mft)、
[VHDの接続](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/attach-vdisk)。

## 通常版の自己展開先が満杯の場合

34269e4のCI36165776900で、配布パッケージのStandard EXEに空き容量ゼロの専用NTFS VHDを自己展開先として渡す試験を追加。ホスト起動失敗と容量解放後の復旧が成功。配布パッケージを先にビルドしてからInvoke-DiskFull.ps1を実行する。ホスト障害コードと、VHDを満杯にしたWin32 112は別々に記録。既存の保存失敗7件も含む8件が成功し、専用VHDの切断と削除を確認した。
