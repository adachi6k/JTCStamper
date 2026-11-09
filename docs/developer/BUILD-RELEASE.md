# 開発環境・ビルド・配布パッケージの作成

## ソースと依存関係

- SDKはglobal.jsonの10.0.403、同じfeature band内のパッチ更新のみ許可する。SDK更新はレビュー付きコミットで行う。
- 現在の製品プロジェクトには外部PackageReferenceがない。.NET／WPFのframeworkとSDKが供給する依存物を使う。将来NuGet依存を追加する際はpackages.lock.jsonをコミットし、CIをlocked modeにする。
- 通常版は選択されたSDKのランタイムパッチを同梱する。実バージョンは生成runtimeconfigから検証し、第三者通知との不一致ではパッケージ生成を停止する。通知を更新して差分レビュー後に再ビルドする。
- Liteは.NET 10 Desktop Runtime x64に依存する。利用者はMicrosoftが提供するサポート中の最新パッチを適用する。
- 月次の.NETセキュリティ更新時と配布前にSDK／ランタイム／将来のNuGet依存の更新を確認する。重要修正は通常版を再ビルドし、両版でスモークを通して修正版を配布する。古い通常版EXEは同梱ランタイムが自動更新されるものではない。
- GitHub ActionsはコミットSHAで固定。更新時はNode実行環境と権限を確認し、成功と未実施項目を保存する。

## 版番号とパッケージ

製品版番号はSemVer 2.0.0に従う。1.0.0以降の互換性の対象・0.xでの運用履歴・公開済み版の不変性は[VERSIONING.md](VERSIONING.md)を参照。配布前にscripts/Test-ReleaseVersion.ps1とscripts/Test-MsixPackaging.ps1を実行する。

Directory.Build.propsのVersionが基準。EXEのProductVersionは「版番号+コミット」、Aboutは版番号・コミット・通常版／軽量版を表示する。AssemblyVersion/FileVersionは.NETの4数値形式なのでプレリリース名を含めない。配布識別にはProductVersionを使用する。

1.0.0では製品版番号`1.0.0`、Gitタグ`v1.0.0`、MSIX番号`1.0.0.0`を対応させる。MSIXは通常版の製品版番号に`.0`を付け、Store提出用ではメジャー番号が0の製品版を拒否する。

変更履歴を更新し、クリーンなコミットへ `v<Version>` のGitタグを付ける。タグpushもWindows CIを実行する。タグ名とVersionが一致しなければ失敗する。タグを作るだけではGitHub ReleaseやPublic化を実施しない。

scripts/New-ReleasePackages.ps1で同じコミットから通常版と軽量版を生成する。build.jsonとchecksums.jsonにソースコミット・タグ・版番号・形式・EXE SHA-256、さらにZIP SHA-256を記録する。ZIPの時刻等まで含むバイト単位の再現ビルドを保証しない。

Windows用のユーザー別インストーラーは通常版ZIPから生成する。Inno Setup 6（CIでは6.7.1）を導入し、`pwsh -File .\scripts\New-WindowsInstaller.ps1 -IsccPath "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"` を実行する。生成先は `dist/installers`。署名済みリリース候補では署名済み通常版ZIPから同じスクリプトで生成し、`-PackageDirectory signed-output -OutputDirectory signed-installer` を指定する。インストーラー本体は署名されない（同梱EXEの署名とは別）。それぞれの `checksums.json` にインストーラーのSHA-256と元のビルド情報を記録する。公開前にはWindowsで導入・更新・アンインストールと履歴・鍵の保持を確認する。

## 成果物と試験の対応

CIは生成されたdistのEXEをそのままWindowsスモークに渡し、起動・再起動・通常版／軽量版交換を検証する。パッケージとレポートのコミットとEXEハッシュを照合して保管する。配布先で取得したZIPのハッシュをchecksums.jsonと比較し、展開後のEXEもbuild.jsonと比較する。

CI成功はOfficeやDPI等を含む全受入試験の成功ではない。未実施項目と実機確認の結果を、その版のリリースチェックリストとIssueに記録して配布可否を判断する。

問題がある配布物は公開を停止し、影響範囲と回避方法をReleaseに記載する。タグを移動して既存成果物を別コードへ付け替えず、修正版には新しいバージョンを使用する。更新・ロールバックでは利用者の鍵と履歴を削除しない。利用者への案内は[DISTRIBUTION.md](../user/DISTRIBUTION.md)にまとめる。

## 開発環境で起動する

Windowsとglobal.jsonで指定した.NET SDKを用意し、リポジトリのルートで実行する。

```powershell
dotnet run --project src/JTCStamper.App -c Release
# 通常版・軽量版のEXEを生成
.\scripts\Publish-App.ps1
# コミット済みソースからZIP・ハッシュを生成
pwsh -File .\scripts\New-ReleasePackages.ps1
```

EXEの出力先はdist/win-x64とdist/lite-win-x64、ZIPとchecksums.jsonはdist/packages。利用者はビルドせず、GitHub Releasesの通常版ZIPから起動できる。

## ZIPの内容と配布時の確認

scripts/New-ReleasePackages.ps1はEXE・README.txt・build.json・LICENSE・NOTICE・THIRD-PARTY-NOTICES.txtを収録する。個人の鍵・履歴は含めない。dist/packages/checksums.jsonでZIP/EXEのハッシュとソースコミットを追跡する。CI署名の手順は[CODE-SIGNING.md](CODE-SIGNING.md)。

CI成功と実Office貼付・クリップボード操作・DPI/Narrator確認は区別し、スキップを合格扱いにしない。利用者向けの起動・更新案内は[DISTRIBUTION.md](../user/DISTRIBUTION.md)に置き、開発コマンドやCI運用を混在させない。

## Microsoft Store向けMSIX

Store版は未公開。ここまでの実装はIssue #18の提出前準備であり、Partner Center登録、名前予約、審査提出、公開操作はアカウント所有者が行う。既存Setup EXE／ZIPは引き続き配布する。認証情報・本人確認書類・秘密鍵は共有しない。

### Windowsで生成する

Windows 11 x64、global.jsonの.NET SDK、PowerShell 7、Windows 10/11 SDK（MakeAppxを含む）が必要。Visual Studioのパッケージング拡張や追加NuGetは不要。`packaging/JTCStamper.Msix.proj`が既存Standardプロファイルをpublishし、生成物の明示的なリストと既存アイコンから作るPNGロゴだけをMSIXに収録する。.NET 10／WPFランタイムを同梱し、別途Desktop Runtimeを必要としない。第三者通知の一致も検査する。

コミット済みのクリーンなソースから、リポジトリのルートで実行する。

```powershell
pwsh -NoProfile -File .\scripts\Test-MsixPackaging.ps1
if ($LASTEXITCODE -ne 0) { throw 'MSIX configuration checks failed.' }
dotnet msbuild .\packaging\JTCStamper.Msix.proj /t:Build
if ($LASTEXITCODE -ne 0) { throw 'MSIX build failed.' }
```

既定は**提出不可のテスト用**。識別名は`JTCStamper.LocalTest`、Publisherは`CN=JTCStamper Local Test`、スタートメニュー表示にも`Local Test`を付ける。出力は`dist/msix/test/*-Test-x64.msix`と`build.json`。未署名なので、そのまま`Add-AppxPackage`でインストールできない。MakeAppxの標準マニフェスト検証を無効にせず、pack後のunpackでStandard EXEのSHA-256も照合する。

`windows-smoke.yml`の独立した`msix`ジョブはWindows Server 2025上でこの生成を実行する。artifact名は`msix-local-test-<commit>`。これは**Windows SDKでのビルド検証**であり、Windows 11のインストール／パッケージ起動／Office検証ではない。`build.json`に識別情報・ソースコミット・SDK・版番号・EXE/MSIXハッシュ・未実施項目を保存する。未署名パッケージのハッシュは後からローカル署名すれば変わる。

製品版番号が`1.1.0-preview.1`や`1.2.3-rc.1`のようなプレリリースの場合、MSIXの構成チェック・生成・artifact保存をスキップし、既存EXE／ZIPのWindows検証は継続する。MSIX生成スクリプト自体は引き続きプレリリース版を拒否する。

保存先の切り替えはパッケージIDの有無だけでは決めない。`GetCurrentPackagePath`のパッケージルートと`AppContext.BaseDirectory`を正規化して比較し、一致した場合だけそのパッケージのLocalStateを使用する。現在のマニフェストと生成処理はEXEをパッケージ直下に置き、この配置は`Test-MsixPackaging.ps1`で検査する。Store識別名の固定値には依存しない。別アプリのパッケージ情報を引き継いだ外部EXE／ZIPは、従来どおりEXEと同じフォルダーを既定とする。Coreチェックではこの場合の既存履歴の読込・追記、鍵・テーマの保持と、名前の前半が同じ別フォルダーを誤認しないことを検証する。

### 予約したStore製品への関連付け

1. 所有者がPartner Centerで個人開発者登録・本人確認・規約同意を行い、「JTC Stamper」を予約する。
2. 製品の「製品ID」にある**Package/Identity/Name、Package/Identity/Publisher、Package/Properties/PublisherDisplayName**を正確に取得する。これらの公開可能な値だけを渡す。
3. `packaging/StoreIdentity.example.json`を`packaging/StoreIdentity.json`へコピーし、3値を置換する。後者はgitignore対象。予約前に架空の提出用情報を設定しない。
4. コミット済みソースからStoreモードで生成する。

```powershell
dotnet msbuild .\packaging\JTCStamper.Msix.proj /t:Build /p:MsixMode=Store
# 別の場所の識別情報ファイルを使う場合
dotnet msbuild .\packaging\JTCStamper.Msix.proj /t:Build /p:MsixMode=Store /p:StoreIdentityFile=C:\Store\JTCStamper.json
```

Storeモードは識別情報なし・サンプルの未置換・Local Test識別情報を拒否する。出力は`dist/msix/store/*-Store-x64.msix`。入力した識別情報はMSIXと`build.json`へ反映されるが、製品の所有権まではスクリプトで検証できない。所有者がPartner Centerとの完全一致を確認する。

MSIX版番号は`Directory.Build.props`の安定版`MAJOR.MINOR.PATCH`を`MAJOR.MINOR.PATCH.0`へ変換する。各数値は65535以下、Store用の末尾は0とし、プレリリース版は拒否する。更新は同じName／Publisherで、既存より大きい版番号を使う。テスト用とStore用は別のパッケージファミリーなので、相互に更新・データ共有されない。

Partner CenterのMSIX提出ではこの**未署名`.msix`**をアップロードする（`.msixupload`である必要はない）。Store提出用に自己署名やテスト証明書を同梱しない。審査・配布時の署名はMicrosoft Storeに委ねる。テスト用artifactを提出しない。

### ローカル署名とWindows 11実機の受入試験

実施記録：[1.0.0 MSIX実機受入（2026-11-09〜10）](../releases/ACCEPTANCE-MSIX-1.0.0-2026-11-10.md)、[移行・ID継承の追加受入（2026-11-10）](../releases/ACCEPTANCE-MSIX-FOLLOWUP-2026-11-10.md)。実施結果と保留事項を記録している。下記チェックリスト全体の完了を示すものではない。

ローカルインストールのためだけに、テスト用MSIXのコピーを署名する。Windows SDKの`signtool.exe`をPATHへ追加し、以下を**使い捨てのテストPC**で実施する。証明書の信頼追加には管理者権限が必要。通常の利用者PCへテスト証明書の導入を案内しない。

```powershell
$build = Get-Content -LiteralPath .\dist\msix\test\build.json -Raw | ConvertFrom-Json
$package = Join-Path .\dist\msix\test $build.File
if ($build.Mode -ne 'Test' -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $build.Sha256) {
    throw 'The test package does not match the latest build.json.'
}
Copy-Item -LiteralPath $package "$env:TEMP\JTCStamper-Test.msix"
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject 'CN=JTCStamper Local Test' -CertStoreLocation Cert:\CurrentUser\My `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256
signtool sign /fd SHA256 /sha1 $cert.Thumbprint "$env:TEMP\JTCStamper-Test.msix"
if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
Export-Certificate -Cert $cert -FilePath "$env:TEMP\JTCStamper-Test.cer"
# 管理者PowerShellで実行（公開証明書だけを信頼する）
Import-Certificate -FilePath "$env:TEMP\JTCStamper-Test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
# 通常のテストユーザーで実行
Add-AppxPackage -Path "$env:TEMP\JTCStamper-Test.msix"
$installed = Get-AppxPackage -Name JTCStamper.LocalTest
Start-Process explorer.exe "shell:AppsFolder\$($installed.PackageFamilyName)!App"
```

署名対象は直前のビルドの`build.json`にある`File`で選び、署名前に`Sha256`を照合する。更新試験でも新しいパッケージの生成後にこの選択・照合を実行し、同じフォルダーに残っている旧版を選ばないようにする。

試験後はアプリを閉じて`Remove-AppxPackage -Package $installed.PackageFullName`を実行し、作成した証明書だけを拇印で確認してCurrentUser\MyとLocalMachine\TrustedPeopleから削除する。PFXや私用データをCI artifactへ含めない。

実機担当は次の各項目にOSビルド、Office版、パッケージ版／ハッシュ、実施日、結果を記録する。失敗・未実施を合格にしない。

- [ ] Desktop Runtime未導入のクリーンなWindows 11 x64でインストールし、スタートメニューから起動。EXEを直接起動するだけの試験で代用しない。
- [ ] 架空の氏名・部署・用途で生成。WordとExcelへクリップボード貼付し、色付き背景上でもPNG背景が透明であることを目視確認。［原本を保存］で保存した`.jtc`内部のPNGも確認。
- [ ] テーマを保存、印面設定を`.jtcstamp`へ保存、用途と注釈付き履歴を生成。終了・再起動後にテーマ・履歴が読め、設定ファイルを読み込めることを確認（印面設定は自動読込ではない）。
- [ ] 履歴、`key.dpapi`、`appearance.json`が`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState`へ保存され、読み取り専用WindowsAppsへ書き込まないことを確認。パッケージ外のエクスプローラーでこの保存先を確認し、`LocalCache\Local\Packages\...`のような二重のパスになっていないことも確認する。［履歴 → 既定の保存先を使用］でもLocalStateに戻ること、DPAPIで既存鍵を再読込できることも確認。
- [ ] テスト用EXE／ZIPをAppData外の専用フォルダーへ展開し、架空の履歴・鍵・テーマを作る。[Invoke-CommandInDesktopPackage](https://learn.microsoft.com/en-us/powershell/module/appx/invoke-commandindesktoppackage?view=windowsserver2025-ps)で別パッケージの情報を持たせてそのEXEを起動し、既存履歴・テーマの読込と追記、［既定の保存先を使用］が同じフォルダーを維持することを確認する。起動したプロセスに実際に別パッケージのIDが付いていることと、別パッケージのLocalStateにJTCStamperの履歴・鍵・テーマを作らないことも確認する。
- [ ] 同じテストName／Publisherと証明書で、版番号を増やした新パッケージを生成・署名し、`Add-AppxPackage`で更新。起動・鍵・履歴・テーマが保持されることを確認。テスト変更は製品の版番号更新とは分ける。
- [ ] [移行手順](../user/DISTRIBUTION.md#store版への切り替え公開前の検証用)でSetup／ZIPからそれぞれ復元し、原本・用途・注釈・設定を照合。同じユーザー用バックアップの復元可否も確認する。
- [ ] ドキュメントなどAppData外へバックアップ後、アンインストールを確認。LocalState外でもAppData内の新規ファイルはMSIXの仮想化により削除され得るため、Setup版の「履歴を残す」と同一視しない。再インストール・AppData外へのバックアップ復元も確認。
- [ ] Store識別情報受領後はStore提出パッケージでもWindows App Certification Kitを実行し、報告を保管。ローカル試験にはPublisherが一致する証明書を使用する。
- [ ] 公開後、実際のStoreから無料で新規インストール・起動・生成・貼付を確認する。

既存`Invoke-WindowsSmoke.ps1`はEXEを一時フォルダーへコピーするため、パッケージID付き起動やLocalStateの試験にはならない。既存Windowsスモークと上記のMSIX試験は別々に実施する。

### Store掲載文・審査メモの下書き

以下は所有者が掲載前に確定する内容で、公開済みの説明ではない。

- 表示名：JTC Stamper（予約が必要）
- 短い説明：日付印を作成し、透過PNGとしてOfficeへ貼り付け。用途と生成履歴もPCに保存。
- 説明：氏名や部署名を入れた日付印を作成できる無料・オープンソースのWindowsアプリです。上段・下段文字、日付を指定し、透過PNGをコピーしてWord・Excel・PowerPointへ貼り付けられます。用途メモと生成履歴をローカルに保存し、暗号化バックアップにも対応します。日本語UI、ライト／ダークテーマを備え、アカウント登録や中央サーバーは不要です。印影照合は試験機能であり、法的な本人認証や貼り付け完了を保証するものではありません。
- スクリーンショット：`docs/images/create-light.png`と`create-dark.png`は既存の架空データの候補。提出版の実画面に更新し、Partner Centerで指定される寸法を確認する。Word／Excelへの貼付例も架空データだけで取得する。
- アイコン：`src/JTCStamper.App/Assets/jtc-icon.png`を元にパッケージの44／150／50px PNGをビルド時に生成。Store掲載用アイコンは同じ元画像からPartner Center指定寸法で用意し、縮小時の見え方を確認する。
- サポートURL：https://github.com/adachi6k/JTCStamper/issues
- プライバシー情報案：アプリは印面文字・用途・履歴・設定を利用者のPCへ保存します。アプリ自身がそれらを開発者や中央サーバーへ送信する機能はありません。クリップボード、エクスポート、バックアップで共有する内容は利用者が管理してください。問い合わせには個人の印影・履歴・鍵を添付しないでください。所有者が公開プライバシーポリシーの内容とURLを確定する（未公開URLを提出しない）。
- 審査メモ：.NET 10 WPFデスクトップアプリのため`runFullTrust`を要求。ローカルファイルへの履歴保存、Windows DPAPIによる鍵保護、Windowsクリップボードへの透過PNGコピーを使用。アカウント不要。テストは架空の文字を入力して［生成してPNGコピー］、Word／ExcelへCtrl+V。課金・広告なしの無料アプリとして提出する。現時点で自動ネットワーク送信や独自アップデーターはない。

所有者が価格「無料」、公開地域、年齢区分、サポート・プライバシー情報を確定し、初回提出と審査対応を行う。公開後の実機確認を終えてからREADMEに**実際のStore製品URL**を追加する。名前予約・識別情報・審査通過が未確認の状態で、Storeから入手できると記載しない。

参考：[WPFのMSIX化](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app)、[MSIX提出](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)、[署名の選択](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)。
