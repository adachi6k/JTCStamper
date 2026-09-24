# 性能評価

Issue #8の評価基準（2026-09-25、実測前に固定）：1,000生成記録でウィンドウ初回描画3秒以内、描画＋3回の生成／コピー記録1秒以内、履歴選択500ms以内を目標とする。操作中の長いUI停止は改善対象とし、重い画像処理は取消し手段を用意する。100件は小規模、10,000件はストレス条件として別記する。

`tests/JTCStamper.Performance`はWindows用の測定プログラム。専用ルートの`.performance-root`マーカーを要求し、新しいGUIDフォルダーに架空の300印面・100/1,000/10,000生成記録を作る。各生成は画像付きGenerated、CopyRequested、CopyCompletedの3記録。通常のHMACで連鎖を作成し、製品のJournalとMainWindowで検証する。

主ウィンドウの生成から描画まで、UIスレッド上のコンストラクターと履歴選択、PNG描画＋3回のAppend、元PNG完全一致、300画像の候補順位付け、1200万画素の赤一色画像を測る。クリップボードはダミーで、利用者のクリップボードを変更しない。したがって実クリップボードAPIやコピー後の画面更新を含むコピー操作全体の時間ではない。

結果はperformance.json。初期試験は各条件1回、作成直後のファイルキャッシュが温まった状態であり、コールド起動・平均・上位パーセンタイルとは扱わない。メモリのピークは同一プロセス内の累積値。判定前にこの制限も記載する。

ビルド例: `dotnet publish tests/JTCStamper.Performance -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`。生成EXEに専用ルートを1引数で渡す。個人の履歴を入力しない。
