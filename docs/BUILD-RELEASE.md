# ビルド・版番号・更新方針

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

## 成果物と試験の対応

CIは生成されたdistのEXEをそのままWindowsスモークに渡し、起動・再起動・通常版／軽量版交換を検証する。パッケージとレポートのコミットとEXEハッシュを照合して保管する。配布先で取得したZIPのハッシュをchecksums.jsonと比較し、展開後のEXEもbuild.jsonと比較する。

CI成功はOfficeやDPI等を含む全受入試験の成功ではない。未実施は#5/#13などに残し、最終配布可否は#1/#2で判断する。

配布撤回・EXEだけの交換・ロールバック・個人データの保全はDISTRIBUTION.mdを参照。タグを移動して既存成果物を別コードへ付け替えず、修正版には新しいバージョンを使用する。

## 開発環境で起動する

Windowsとglobal.jsonで指定した.NET SDKを用意し、リポジトリのルートで実行する。

```powershell
dotnet run --project src/JTCStamper.App -c Release
# 通常版・軽量版のEXEを生成
.\publish-all.cmd
# コミット済みソースからZIP・ハッシュを生成
pwsh -File .\scripts\New-ReleasePackages.ps1
```

EXEの出力先はdist/win-x64とdist/lite-win-x64、ZIPとchecksums.jsonはdist/packages。利用者はビルドせず、GitHub Releasesの通常版ZIPから起動できる。
