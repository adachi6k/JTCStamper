# 公開物とライセンスの確認

## 採用条件

ソースはApache-2.0。LICENSEとNOTICEを配布し、同梱.NETの通知をTHIRD-PARTY-NOTICES.txtに収録する。貢献者の一定の特許許諾を明示するが、第三者の知財侵害を保証・免責するものではない。

第三者通知は通常版publish後のruntimeconfigに記載された実際の同梱ランタイムとNuGetパッケージから生成する。scripts/Update-ThirdPartyNotices.ps1 -Checkを配布生成時に実行し、ランタイム更新時の通知更新漏れを失敗にする。通知更新後は差分をレビューしてコミットする。

印影はインストール済みWindowsフォントを参照して描画する。フォントファイルは同梱しない。アイコンは開発中にこのプロジェクト向けに生成したもの。依存ライブラリの権利をプロジェクトのライセンスへ変更しない。

## 実物印影の履歴除去

ユーザー承認に基づき、個人の実物印影を含むreference/JTC-Stamper-Angle-PoC.zipを全39コミットから削除してmainを更新した。現在の到達可能な履歴には対象ファイルとblobがない。再追加を.gitignoreで防ぐ。利用者のDownloadsにある元資料は削除しない。

2026-09-25の確認では、GitHubの古いblob APIは対象をまだ返した。履歴の書換えだけではサーバー側の参照・キャッシュの完全消去を証明できない。所有者は「force pushで履歴から消えていたら気にしない」と判断したため、Supportによる残存データ削除は公開の必須条件から外す。2026-09-26にmainと既存の全公開予定タグの到達可能な履歴へ対象ZIP/blobがないことを再確認した。成果物への再混入は引き続き禁止し、その他の試験・公開前レビューが完了するまでPrivateを維持する。

参考: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository

## 配布内容と署名

配布ZIPの許可リストはEXE、README.txt、build.json、LICENSE、NOTICE、THIRD-PARTY-NOTICES.txtの6ファイル。鍵・履歴・個人設定・私的画像は入れない。CIの試験成果物も許可リストを維持する。

初回プレビューは未署名EXEを前提に評価する。配布予定経路はGitHub Releases。まだ公開・リリースしていない。SmartScreen、Smart App Control、組織ポリシーは実ダウンロード経路で未検証。保護設定の無効化を案内しない。署名を導入しても警告が必ず消えるとは保証しない。

最終ZIPのハッシュと試験結果を紐付けて#6へ記録し、#9と親#1が完了するまでPrivateを維持する。

## preview.11タグの再確認

2026-09-26、acad6e0までの77コミット・598 blobとリモートmain/タグを照合。除去対象の実印影ZIP/blobは到達不能、資格情報の代表パターン/鍵・履歴ファイル/機械固有パスの該当なし。限界は前述の通り。タグCI36166583336のZIPを検査し6ファイル許可リスト、EXE/ZIP/CIハッシュ、未署名を確認。両ZIPとchecksums.jsonをPrivateのReleaseドラフトへ配置し、まだ公開していない。

[Microsoftの.NET10リリース情報](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)を同日に再確認。最新SDK10.0.401、runtime10.0.12（2026-09-08、security=true、active）と一致。将来の更新監視やすべての脆弱性の不存在を保証するものではない。実ブラウザー経由の警告確認は所有者へ依頼済み。

## 実ダウンロード経路の所有者確認

Standard版preview.11を案内したブラウザー取得・展開・起動の確認で、初回に発行元不明で起動を止める趣旨の警告があったと回答を受領。その後のアプリ操作は成功。詳細文面の記録がないためSmartScreen/SAC/UAC等の種別を断定しない。保護設定の変更も推定しない。未署名であることは既存の成果物検査で確認済み。全Windows設定・組織ポリシーの試験完了や、警告なしの起動としては扱わない。
