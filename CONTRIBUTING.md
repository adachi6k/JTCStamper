# 開発者ガイド

JTC Stamperのソースをビルド・改修・検証する方向けの入口です。アプリを使うだけの場合は[README](README.md)の利用者向けガイドを参照してください。

## ビルドと配布

- [開発環境・ローカル起動・パッケージ作成](docs/developer/BUILD-RELEASE.md)
- [コード署名とCI署名](docs/developer/CODE-SIGNING.md)
- [バージョンと互換性の規約](docs/developer/VERSIONING.md)

## 設計とデータ形式

- [全体設計](docs/developer/DESIGN.md)
- [照合アルゴリズム・配点・入力制限](docs/developer/VERIFICATION-TECHNICAL.md)
- [20bit幾何コード](docs/developer/RING20-CODE.md)
- [生成時の用途メモ](docs/research/GENERATION-CONTEXT.md)
- [バックアップと失敗時の扱い](docs/developer/BACKUP-TECHNICAL.md)
- [別PC移行用ファイルの暗号方式・形式](docs/developer/PORTABLE-BACKUP.md)

## 試験と変更の進め方

[Windows自動試験](docs/developer/WINDOWS-SMOKE.md)と[0.1.0の評価記録](docs/archive/0.1.0/VALIDATION.md)を参照してください。過去の評価結果は、記載されたコミット・印影形式・入力条件に対する結果です。新版へそのまま適用しません。EMFの試験は[採用見送りの記録](docs/research/EMF-PLAN.md)として保持しています。

変更時は問題と変更後の動作、確認した試験、未確認事項をPRへ記載してください。保存形式・符号形式を変更する場合は、旧データの読取可否と移行の有無を変更履歴に明記します。

試験には合成の印影・履歴・鍵を使用します。個人の実物印影、履歴、秘密鍵、署名用PFXをコミットしないでください。

ソースは[Apache-2.0](LICENSE)です。依存物の追加・更新では[第三者通知](THIRD-PARTY-NOTICES.txt)も確認してください。

## リポジトリの配置

| 場所 | 役割 |
|---|---|
| `src/` | 製品のWPFアプリとCore |
| `tests/` | 回帰・障害・受入・性能の検証 |
| `experiments/` | 符号・外観などの研究用コード |
| `scripts/` | ビルド・配布・署名・試験の入口 |
| `.github/workflows/` | 実行するCI定義の正本 |
| `docs/` | [用途別の資料](docs/README.md) |
| `dist/` | 生成されたEXE・ZIP（Git管理外） |

SDK・製品版番号の設定とライセンス類はルートに置きます。`bin/`・`obj/`・ルートの`test-results/`は生成物です。古い`publish/`から起動する`start.cmd`と重複CMDは廃止しました。配布アプリはZIPをリポジトリ外へ展開して使用してください。
