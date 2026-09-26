# 自己署名によるコード署名

公開済み0.1.0は未署名のまま変更しない。通常のCI配布物は未署名。配布用の自己署名は専用の「Signed release candidate」Actionsで行う。公開済み0.1.0の配布物は差し替えない。

自己署名は署名後の改変と証明書の継続性を確認するためのもの。発行者の実在を第三者が認証するものではなく、一般のWindowsでは既定で信頼されない。SmartScreenは署名の有無だけでなく評判も評価するため、自己署名で警告が消えるとは案内しない。

- [Microsoft: SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [Microsoft: Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)

## ローカル試験用の証明書を作成する（Windows）

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

今回の自己署名モードにはタイムスタンプを付けない。証明書期限後も署名が有効とは保証しない。本番向けの第三者証明書/タイムスタンプ/鍵保管サービスは別途導入する。ローカル試験用の非エクスポート可能鍵はGitHub Secretsへ移せないため、以下のCI用別鍵を使用する。

配布ZIPには秘密鍵を含めない。CERを共有する場合は公開証明書と明示し、別のRelease assetとして証明書SHA-256と共に公開する。CER配布は信頼登録を意味しない。

## 検証

`Test-CodeSigning.ps1`はCIの一時証明書でコピーしたEXEを署名し、期待する未信頼ルート状態、PE本体の改変拒否、不正な拇印の拒否を検査する。信頼ストアを変更せず、一時証明書と秘密鍵はfinallyで削除する。テスト用の署名EXEや証明書は配布しない。


## Actionsで署名付き候補を作る（現在の配布方針）

GitHub Actionsの **Signed release candidate** をmainで手動実行する。ワークフローはmain以外ではジョブを実行しない。Environment `release-signing`もmainブランチだけを許可する。PRイベントでは実行しない。

1. 鍵を持たないbuildジョブが同一SHAでビルド・Core/Windows試験を行い、未署名ZIPを渡す。
2. signジョブだけがEnvironmentのSecretsを使う。ZIPの許可リスト、ソースSHA、EXE/ZIPハッシュを確認して署名する。
3. インポートした証明書/秘密鍵と一時PFXをfinallyで削除。環境の秘密値もクリアする。
4. 鍵削除後に、署名付きZIPのハッシュと署名者を検査し、両EXEの起動・再起動等を試験する。
5. 成功したときだけ `signed-packages-<SHA>` に両ZIP・checksums.json・公開CERを保存。公開Releaseへの添付は別工程で、Actionsは自動公開しない。

通常のWindows build and smokeワークフローのpackagesは未署名なので、配布時に取り違えない。署名済み成果物の再ビルド・再署名はハッシュが変わるため、検証済みの同じZIPを使う。

### Environment設定

- Secret `SIGNING_PFX_BASE64`: パスワード付きPFXのBase64（Base64自体は暗号化ではない）。
- Secret `SIGNING_PFX_PASSWORD`: PFXパスワード。
- Variable `SIGNING_CERTIFICATE_SHA256`: 公開証明書のSHA-256。インポートした鍵の取り違えを拒否する。
- Deployment branch rule: **branch mainのみ**。手動実行権限とmainへの書込み権限を持つ管理者を信頼する構成。

Secretsは署名ステップだけの環境変数へ渡す。コマンド引数・ログへ値を出さず、リポジトリやArtifactにPFX/パスワードを格納しない。ActionsはコミットSHA固定、checkoutの資格情報保持なし、GITHUB_TOKENはcontents:readのみ。

### CI鍵の保管と更新

CI用にはエクスポート可能な別の自己署名証明書を作成した。初期の非エクスポート可能なローカル証明書とは別鍵。署名付き版はまだ公開していないため、公開済み署名者の変更にはあたらない。

管理者PCの `%LOCALAPPDATA%\JTCStamperSigning` に、パスワード付きPFXと、パスワードをDPAPI CurrentUserで保護した復旧データを保持する。Git/OneDrive内には置かない。この復旧データは同じWindowsユーザー環境向けであり、別PCへの移行用バックアップではない。PC廃棄前には別途安全な鍵保管/移行を行う。平文パスワードをチャットやIssueへ貼らない。

鍵の更新時はPFX・パスワード・公開SHA-256変数をそろえて更新し、公開証明書の指紋変更を記録する。自己署名のためSmartScreenの警告が消えるわけではない。タイムスタンプは引き続き未対応。

### 初回移行の検証記録

[初回署名Actions](https://github.com/adachi6k/JTCStamper/actions/runs/36220519167)はソース4dbc84c9e731e2a339a18ff5fdbb395dc247989fでbuild/signとも成功。インポート鍵削除後の署名済み両EXEのWindows試験まで通過。

CI証明書のSHA-256: `66BEC3F61F49DEFE6CC73A0AD9BA25794C9728A3DBFA64CEBBFA38A63AB8DE78`

CI証明書の拇印: `005CC7473464F11A33341671B33DCCA4CE79880E`

公開済み0.1.0は未署名のまま不変。署名候補はActions Artifactとして保存し、この移行では新しいReleaseを公開していない。
