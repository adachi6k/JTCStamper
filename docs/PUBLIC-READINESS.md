# 公開物とライセンスの確認

## 採用条件

ソースはApache-2.0。LICENSEとNOTICEを配布し、同梱.NETの通知をTHIRD-PARTY-NOTICES.txtに収録する。貢献者の一定の特許許諾を明示するが、第三者の知財侵害を保証・免責するものではない。

第三者通知は通常版publish後のruntimeconfigに記載された実際の同梱ランタイムとNuGetパッケージから生成する。scripts/Update-ThirdPartyNotices.ps1 -Checkを配布生成時に実行し、ランタイム更新時の通知更新漏れを失敗にする。通知更新後は差分をレビューしてコミットする。

印影はインストール済みWindowsフォントを参照して描画する。フォントファイルは同梱しない。アイコンは開発中にこのプロジェクト向けに生成したもの。依存ライブラリの権利をプロジェクトのライセンスへ変更しない。

## 実物印影の履歴除去

ユーザー承認に基づき、個人の実物印影を含むreference/JTC-Stamper-Angle-PoC.zipを全39コミットから削除してmainを更新した。現在の到達可能な履歴には対象ファイルとblobがない。再追加を.gitignoreで防ぐ。利用者のDownloadsにある元資料は削除しない。

ただし2026-09-25の確認では、GitHubの古いblob APIは対象をまだ返した。履歴の書換えだけではサーバー側の参照・キャッシュの除去を証明できない。Public化を保留し、GitHub側の除去と再確認をIssue #9で追跡する。

参考: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository

## 配布内容と署名

配布ZIPの許可リストはEXE、README.txt、build.json、LICENSE、NOTICE、THIRD-PARTY-NOTICES.txtの6ファイル。鍵・履歴・個人設定・私的画像は入れない。CIの試験成果物も許可リストを維持する。

初回プレビューは未署名EXEを前提に評価する。配布予定経路はGitHub Releases。まだ公開・リリースしていない。SmartScreen、Smart App Control、組織ポリシーは実ダウンロード経路で未検証。保護設定の無効化を案内しない。署名を導入しても警告が必ず消えるとは保証しない。

最終ZIPのハッシュと試験結果を紐付けて#6へ記録し、#9と親#1が完了するまでPrivateを維持する。
