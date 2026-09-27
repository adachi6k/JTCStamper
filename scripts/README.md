# 開発・配布用スクリプト

リポジトリのルートからPowerShellで実行します。詳細は[ビルドガイド](../docs/developer/BUILD-RELEASE.md)。

| 入口 | 用途 |
|---|---|
| `Publish-App.ps1 [-Variant All/Standard/Lite]` | 開発中のEXE生成。既定は両版 |
| `New-ReleasePackages.ps1` | コミット済みソースから配布ZIP・ハッシュを作成 |
| `Invoke-WindowsSmoke.ps1` | 配布EXEのスモーク |
| `Invoke-DiskFull.ps1` | 専用VHDの容量不足試験（管理者権限が必要） |
| `Test-ReleaseVersion.ps1` | バージョン規約のチェック |
| `Test-CodeSigning.ps1` | 試験用証明書による署名検証 |
| `New-CodeSigningCertificate.ps1` | 署名用証明書作成 |
| `Sign-ReleaseExecutable.ps1` | EXE署名 |
| `Invoke-CiSigning.ps1` | 保護されたCI環境での署名 |
| `Update-ThirdPartyNotices.ps1` | 第三者通知の生成・検査 |

`ReleaseVersion.ps1`は他のスクリプトが読み込む共通関数です。開発起動は`dotnet run --project src/JTCStamper.App -c Release`を使います。実験用の画像生成器は[experiments/fixtures](../experiments/fixtures/)にあります。
