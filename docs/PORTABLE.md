# 単一EXE配布版（Windows x64）

`dist/win-x64/JTCStamper.App.exe`だけを配布できます。アイコン・WPF・.NET 10.0.12を含む実行環境を同梱しています。Windows x64用でありARM64ネイティブ版ではありません。

## 使用と更新

書込み可能な専用フォルダーにEXEを置いて実行してください。起動後にEXEの隣へ`key.dpapi`と`journal`が作成されます。印面設定は［ファイル］から任意の場所へ保存します。

更新時はアプリを終了し、同じフォルダーのEXEのみ置き換えます。`key.dpapi`、`journal`、設定ファイルは残してください。既存の`publish`フォルダーにある版から更新する場合も同じです。今回、既存`publish`のEXE・ログ・鍵は変更せず、配布物を`dist/win-x64`へ別途配置しています。

新しい場所で起動すると、その場所で新しい履歴が始まります。既存履歴を使いたい場合は［履歴］から元の保存先を選択できます（起動中のみ）。DPAPI鍵の別PC移行制約は従来どおりです。利用済みフォルダー全体を他人へ渡さず、未使用の配布用EXEのみ渡してください。

ネイティブのWPFライブラリは実行時に.NET管理の一時領域へ展開される場合があります。履歴保存先にはAppContext.BaseDirectoryを使い、EXEの隣を参照します。

## 再ビルド

.NET 10 SDKがあるWindowsで`publish-portable.cmd`を実行します。実際の設定は`src/JTCStamper.App/Properties/PublishProfiles/Portable.pubxml`です。

単一ファイル・自己完結・ネイティブライブラリ同梱を有効化。トリミングと圧縮は無効です。デバッグ情報はアセンブリに埋め込み、PDBを別配布する必要をなくしています。

## 実施した検証

- Linux上の.NET SDK 10.0.401でWindows x64向けpublish成功。
- 空の出力先にEXEひとつだけが出力されることを確認。
- PE形式がWindows x64 GUI、ICON/GROUP_ICONリソースあり。
- バンドル内384エントリーの範囲・必須DLLを検査。
- ネイティブコードの.textセクションがSDKのsinglefilehostと一致。
- 同梱runtimeconfigが.NETとWindowsDesktop 10.0.12の自己完結構成。
- 履歴・秘密鍵・個人設定をバンドルに含めていないことを確認。
- Coreの自動テスト10件通過（表示・Windows APIのテストではありません）。
- 配置後のSHA-256一致を確認。数値はPORTABLE-VALIDATION.json参照。

## 未確認

単一EXEのWindows実機起動、アイコン表示、PNGコピー、DPAPI、初回展開、既存履歴を伴うEXE置換は未確認です。ユーザーがリモートのため、確認依頼は行っていません。コード署名は未実施です。

## 参考

- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs
- https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs
