# 開発環境・ビルド・配布パッケージの作成

## ソースと依存関係

- SDKはglobal.jsonの10.0.401、同じfeature band内のパッチ更新のみ許可する。SDK更新はレビュー付きコミットで行う。
- 現在の製品プロジェクトには外部PackageReferenceがない。.NET／WPFのframeworkとSDKが供給する依存物を使う。将来NuGet依存を追加する際はpackages.lock.jsonをコミットし、CIをlocked modeにする。
- 通常版は選択されたSDKのランタイムパッチを同梱する。実バージョンは生成runtimeconfigから検証し、第三者通知との不一致ではパッケージ生成を停止する。通知を更新して差分レビュー後に再ビルドする。
- Liteは.NET 10 Desktop Runtime x64に依存する。利用者はMicrosoftが提供するサポート中の最新パッチを適用する。
- 月次の.NETセキュリティ更新時と配布前にSDK／ランタイム／将来のNuGet依存の更新を確認する。重要修正は通常版を再ビルドし、両版でスモークを通して修正版を配布する。古い通常版EXEは同梱ランタイムが自動更新されるものではない。
- GitHub ActionsはコミットSHAで固定。更新時はNode実行環境と権限を確認し、成功と未実施項目を保存する。

## 版番号とパッケージ

製品版番号はSemVer 2.0.0に従う。互換性の対象・0.xでの運用・公開済み版の不変性は[VERSIONING.md](VERSIONING.md)を参照。配布前にscripts/Test-ReleaseVersion.ps1を実行する。

Directory.Build.propsのVersionが基準。EXEのProductVersionは「版番号+コミット」、Aboutは版番号・コミット・通常版／軽量版を表示する。AssemblyVersion/FileVersionは.NETの4数値形式なのでプレリリース名を含めない。配布識別にはProductVersionを使用する。

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
