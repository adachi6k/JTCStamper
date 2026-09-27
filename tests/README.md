# 製品の検証

| プロジェクト | 対象 |
|---|---|
| JTCStamper.Checks | Coreの回帰チェック |
| JTCStamper.StorageFailures | 保存中断と復旧 |
| JTCStamper.DiskFull | 専用VHDによる容量不足 |
| JTCStamper.Migration | 別Windows環境への移行 |
| JTCStamper.WindowsAcceptance | Windows・クリップボード・Officeの受入試験 |
| JTCStamper.Performance | 履歴の負荷・性能測定 |

実行条件と安全な手順は[Windows試験ガイド](../docs/developer/WINDOWS-SMOKE.md)を参照してください。CI定義の正本は[.github/workflows](../.github/workflows/)です。製品のWPFスモークは`src/JTCStamper.App/SmokeTest.cs`にあります。

符号方式の比較や不採用案の評価は[experiments](../experiments/README.md)へ分離しています。
