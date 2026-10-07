# 0.2.1 リリース確認

正式版番号は0.2.1。通常版のユーザー別インストーラーを追加し、0.2.0の印影・履歴形式と画像照合の仕様を維持する。2026-10-07に所有者から最終実機確認の完了報告と文書整備・公開の指示を受領した。

## 配布対象

ソースコミットは `76ce50d816ede804787ae632f998f61a4b958255`。既存タグ `v0.2.1` はこのコミットを指す。署名Actionsで作成・検証済みの同じ成果物を配布する。公開記録の文書更新はmainへ別コミットとして追加し、タグの移動・配布物の再ビルド・再署名は行わない。

- 通常版ZIP、Lite版ZIP、通常版Setup.exe
- 公開証明書 `JTCStamper-signing.cer`
- ZIP用 `checksums.json` とSetup用 `installer-checksums.json`。後者は元の `signed-installer/checksums.json` のファイル名だけを変更する。

アプリEXEは自己署名、Setup.exe本体は未署名。証明書の有効期限は2029-09-26 05:18:25 UTC、タイムスタンプなし。自己署名でWindowsの警告が解消するとは案内しない。

## 公開前の確認

- [x] [対象コミットのWindows CI](https://github.com/adachi6k/JTCStamper/actions/runs/36734352267)が成功。
- [x] [署名と署名後のWindows試験](https://github.com/adachi6k/JTCStamper/actions/runs/36734448895)が成功。
- [x] 通常CIでインストール・起動・アンインストールとテスト用履歴・鍵の保持を確認。
- [x] 最終候補のWindows実機確認について所有者から完了報告を受領。
- [x] 配布ZIP・アプリEXE・SetupのSHA-256、ZIP内のbuild.json、ソースコミット、公開証明書のSHA-256を確認。
- [x] 通常版・Lite版ZIPのライセンス・第三者通知の同梱を確認。
- [x] CHANGELOGと[公開用リリースノート](NOTES-0.2.1.md)を確定。

## 実機確認の記録

2026-10-07、所有者が最終候補の実機確認を完了したと報告。確認項目は、署名済みEXEを含むSetupによる導入・更新・削除、履歴と鍵の保持、0.2.0からのバックアップ復元、PNGコピー。これは所有者の完了報告を記録したもので、この公開作業で実機試験を再実行した記録ではない。CIのクリップボード試験は省略のままとする。

## 公開状況

2026-10-07 09:30 JSTに [v0.2.1 Release](https://github.com/adachi6k/JTCStamper/releases/tag/v0.2.1) を正式版として公開し、Latestに設定した。

- [x] 公開本文が[確定リリースノート](NOTES-0.2.1.md)と一致することを確認。
- [x] 全6添付ファイルのGitHub側SHA-256がローカルの検証済み成果物と一致。
- [x] 公開Releaseから全6ファイルを再ダウンロードし、SHA-256が一致。
- [x] 再ダウンロードした両ZIP内のアプリEXEハッシュとbuild.jsonも一致。

CHANGELOG・リリースノート・本確認記録のみを公開記録としてmainに追加する。配布元コミットとタグは `76ce50d816ede804787ae632f998f61a4b958255` のまま維持する。
