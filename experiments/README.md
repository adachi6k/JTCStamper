# 実験用コード

製品の回帰テストとは分けた、符号容量・外観・読取精度・Office経路の評価ツールです。配布物には含めません。採否と過去の結果は[研究記録](../docs/research/README.md)にあります。

- `JTCStamper.CapacityEvaluation`：符号容量の比較
- `JTCStamper.AlphaEvaluation` / `ColorLineEvaluation` / `RedundancyEvaluation`（いずれもJTCStamper.接頭辞付き）：濃淡・色・冗長化の検討
- `JTCStamper.ImageEvaluation` / `JTCStamper.HeldOutEvaluation`：合成画像・独立入力の評価
- `JTCStamper.EmfEvaluation`：採用を見送ったEMFの実験。Officeを操作するため通常の試験には含めません。再実施が必要な場合は同フォルダーの`Invoke-EmfOfficeSafe.ps1`を参照してください。
- `fixtures/`：Pillowによる入力画像生成器。製品レンダラーではありません。

旧12bit用の生成器も検討の再現用に残しています。現在の20bit読取器との組合せで旧結果を再現できるとは限りません。厳密な再現には、結果資料に記載された当時のコミットを使ってください。過去の結果JSON内のソースパス・ハッシュは当時の値を保持しています。

プロジェクトは以前の`tests/<プロジェクト名>`から同じ深さの`experiments/<プロジェクト名>`へ移動しました。生成器は`scripts/Generate-*Fixtures.py`から`experiments/fixtures/`へ移動しました。出力先はリポジトリ外の専用作業フォルダーを指定してください。
