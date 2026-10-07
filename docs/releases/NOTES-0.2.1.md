日付印を作り、用途と生成履歴を残せるWindowsアプリです。0.2.1では通常版のインストーラーを追加しました。

## ダウンロード

Windows 11 x64対応です。

- **Standard Setup.exe**：通常版インストーラー。必要な.NETを同梱し、管理者権限なしで導入できます。
- **Standard ZIP**：通常版。必要な.NETを同梱しています。書き込み可能なローカルフォルダーへ展開し、`JTCStamper.App.exe`を起動してください。
- **Lite ZIP**：軽量版。.NET 10 Desktop Runtime（x64）が必要です。

## 0.2.1の変更

- ユーザー別のSetup.exeインストーラーを追加しました。既定の配置先は `%LOCALAPPDATA%\Programs\JTCStamper` です。ショートカットから起動できます。
- アンインストールしても履歴の `journal` と鍵の `key.dpapi` は保持します。
- ZIP版・Lite版も引き続き提供します。印影形式・履歴形式・画像照合の仕様は0.2.0から変更していません。

## 更新とバックアップ

更新前にバックアップを取り、アプリを終了してください。インストーラー版の更新は新版Setup.exeを実行します。

0.2.0の保存データは継続利用できます。ただし、ZIP版からインストーラー版へ移ると保存先が変わります。旧ZIPフォルダーを残し、必要な履歴はアプリのバックアップ・復元操作で移してください。ZIP版とインストーラー版で同じ保存先を共有しないでください。

**0.1.0の旧12bit印影・原本・生成履歴は0.2.1でも照合対象外です。自動変換はありません。** 旧データを参照する場合は0.1.0を別フォルダーに残し、保存先を分けてください。保存ファイルや鍵を自動削除することはありません。プレーン形式は継続対応です。

[使い方](https://github.com/adachi6k/JTCStamper/blob/v0.2.1/README.md) · [更新手順](https://github.com/adachi6k/JTCStamper/blob/v0.2.1/docs/user/DISTRIBUTION.md) · [バックアップ](https://github.com/adachi6k/JTCStamper/blob/v0.2.1/docs/user/BACKUP.md)

## 署名

通常版・Lite版のアプリEXE、およびSetupに同梱するアプリEXEは自己署名済みです。**Setup.exe本体は未署名です。** 自己署名は一般のWindowsでは既定で信頼されず、SmartScreenなどの警告が出る場合があります。

署名者は `CN=JTC Stamper CI (Self-signed)`、証明書の有効期限は2029-09-26 05:18:25 UTCです。タイムスタンプは付与していません。添付の `JTCStamper-signing.cer` は公開証明書で、秘密鍵を含みません。証明書の信頼登録は求めません。

公開証明書のSHA-256：

```text
66BEC3F61F49DEFE6CC73A0AD9BA25794C9728A3DBFA64CEBBFA38A63AB8DE78
```

## 照合について

画像照合は履歴候補を探す試験機能です。1.000は候補との比較点数で、保存画像のコピーは見分けられません。記録対象は生成・コピーであり、貼付完了を意味しません。出力はPNGです。

## 配布確認

ソース：`76ce50d816ede804787ae632f998f61a4b958255`（`v0.2.1`）。

[Windows CI](https://github.com/adachi6k/JTCStamper/actions/runs/36734352267)と[署名・署名後試験](https://github.com/adachi6k/JTCStamper/actions/runs/36734448895)が成功しています。通常CIではインストール・起動・アンインストールとテスト用データの保持を確認しています。CIで省略するクリップボード操作を含む最終候補の実機確認は、2026-10-07に所有者から完了報告を受領しました。

添付の `checksums.json` はZIPとアプリEXEのハッシュ、`installer-checksums.json` はSetupのハッシュと同梱アプリのビルド情報です。`installer-checksums.json` の `Build.Signing` は同梱アプリの署名を示し、Setup本体の署名ではありません。

### 配布ファイルのSHA-256

`JTCStamper-0.2.1-Standard-win-x64.zip`

```text
162637AAAA761F7E488DF483620300F8AB1212DEC3EFC6B4F759486A186EA263
```

`JTCStamper-0.2.1-Lite-win-x64.zip`

```text
19B80C29D0620572BB46F4060FB3590D6106F372F58BC61CBB5412439AD3F1C7
```

`JTCStamper-0.2.1-Standard-win-x64-Setup.exe`

```text
29C6BA33567F8B317F5E6DE70C0B1507F2C8A1F86CF7B0FDD99AA3B8E6C5A0E1
```
