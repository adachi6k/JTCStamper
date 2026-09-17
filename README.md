# JTC Stamper — 最小実装

**Just To Confirm — 確認した、その記録を。**

JTCは「Just To Confirm」の略です。

Windows用の日付印アプリ。C# / .NET 10 / WPF。

## 起動

Windows x64では、`start.cmd`を実行してください。`publish`内にランタイム同梱のビルド済みアプリを用意しています（Windows実機では未検証）。

ソースから起動する場合は.NET 10 SDKをインストールして`run.cmd`を実行します。

```powershell
dotnet run --project src/JTCStamper.App -c Release
```

`publish/JTCStamper.App.exe`を直接起動することもできます。

## 実装した機能

- 赤い印影のプレビュー、氏名・任意の日付・下段文字の変更。
- PNGクリップボード出力（Bitmap互換形式も登録）。
- 生成ごとのUUID、生成UTCと表示日付の分離、生成・コピー要求・コピー結果の記録。
- HMAC-SHA256の連鎖履歴、DPAPI CurrentUserによる鍵保護。
- 生成履歴一覧、元記録を変更しない注釈追記。
- 完全なイベントID・HMACを持つ`.jtc`原本の保存。
- 保存失敗時はコピーを成功扱いにしない。

履歴は`%LOCALAPPDATA%\JTCStamper`に保存します。コピー完了の記録は貼付完了を意味しません。

## 未実装・未検証

EMF、Office経由の実機検証、幾何コード、画像からの照合、原本取込、暗号化バックアップ・移行UIは次段階です。最小版で模倣検出ができるという意味ではありません。Windowsの画面・DPAPI・クリップボードは実機動作確認が必要です。

鍵を失うと既存履歴のHMACを検証できません。DPAPI鍵ファイルだけの別PCコピーでは移行できません。正式運用前にバックアップ・復元機能を実装して検証してください。

## 文書

- [設計](docs/DESIGN.md): データ、失敗時の挙動、鍵の保管と移行、照合の3結果、段階的な実装方針。
- [検証計画](docs/VALIDATION.md): Windows/Office受入試験、正規復号率と模倣誤受入率を分けた評価。
- [元のREPORT](reference/REPORT.md): 添付PoCの観測結果。参考資料として保存。
- [自動検証結果](docs/TEST-RESULTS.txt)。

## 次の順序

1. 最小版のWindows実機確認と改善。
2. 共通描画シーン、EMF、Office貼付試験。
3. 2-bitの生成・既知切り出し復号・棄却判定、履歴候補検索。
4. 領域探索と追加特徴、12-bit容量の可否を実験で判断。
5. 独立評価セットで正規復号率と模倣誤受入率を測定。
6. バックアップ復元、移行、配布署名など製品運用の整備。
