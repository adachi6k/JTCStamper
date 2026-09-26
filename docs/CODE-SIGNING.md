# 自己署名によるコード署名

公開済み0.1.0は未署名のまま変更しない。自己署名は次版の任意ビルド機能。通常のCI配布物は、証明書を明示しない限り未署名になる。

自己署名は署名後の改変と証明書の継続性を確認するためのもの。発行者の実在を第三者が認証するものではなく、一般のWindowsでは既定で信頼されない。SmartScreenは署名の有無だけでなく評判も評価するため、自己署名で警告が消えるとは案内しない。

- [Microsoft: SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [Microsoft: Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)

## 証明書を作成する（Windows）

```powershell
./scripts/New-CodeSigningCertificate.ps1 -PublicCertificatePath C:\YourOutput\JTCStamper.cer
```

RSA 3072 / SHA-256、コード署名用EKU、有効期限3年。秘密鍵はCurrentUser\Myに非エクスポート可能として保存する。出力は公開証明書CERだけ。表示された拇印を記録し、後続ビルドで同じ証明書を明示する。出力先フォルダーは事前に用意する。

信頼済みルート・信頼された発行元には自動登録しない。利用者にSmartScreenの無効化や証明書の無条件な信頼を求めない。

このローカル鍵はPFXとしてバックアップできない。Windowsプロファイル/鍵を失うと同じ鍵での署名はできなくなる。再発行時には新しい証明書の指紋を告知する。証明書を毎回作り直してはならない。署名の秘密鍵は印影履歴のHMAC鍵とは別。

## 署名付き配布を作成する

ソースをコミットしたうえで実行する。PowerShellの実行ポリシーや管理者の方針は尊重し、スクリプトから変更しない。

```powershell
./scripts/New-ReleasePackages.ps1 -SigningCertificateThumbprint '<作成時の40桁の拇印>'
```

publish → 自己署名 → 署名の検査 → EXEハッシュ計算 → ZIP → ZIPハッシュの順。署名後の変更は不可。公開済みの版/ZIPを再署名して上書きしない。build.jsonとchecksums.jsonに署名者・証明書SHA-256・有効期限・信頼状態を記録する。

今回の自己署名モードにはタイムスタンプを付けない。証明書期限後も署名が有効とは保証しない。本番向けの第三者証明書/タイムスタンプ/管理されたCI秘密鍵は別途導入する。現行の非エクスポート可能鍵をGitHub Secretsへ移すことはできない。

配布ZIPには秘密鍵を含めない。CERを共有する場合は公開証明書と明示し、別のRelease assetとして証明書SHA-256と共に公開する。CER配布は信頼登録を意味しない。

## 検証

`Test-CodeSigning.ps1`はCIの一時証明書でコピーしたEXEを署名し、期待する未信頼ルート状態、PE本体の改変拒否、不正な拇印の拒否を検査する。信頼ストアを変更せず、一時証明書と秘密鍵はfinallyで削除する。テスト用の署名EXEや証明書は配布しない。
