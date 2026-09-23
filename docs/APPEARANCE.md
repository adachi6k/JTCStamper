# 外観とテーマ（2026-09-24）

## 使い方

左サイドバーの「作成」「照合」「履歴」で切り替えます。作成画面の直近3件と、行から履歴への移動は維持しています。各画面を再生成しないため、画面移動で入力や照合結果を捨てません。

［表示 → テーマ］で「Windowsの設定に合わせる」「ライト」「ダーク」を選択します。初期値はWindows設定。変更は即時反映し、起動したEXEと同じフォルダーのappearance.jsonに保存します。履歴保存先を切り替えても、外観設定の保存先は変わりません。設定ファイルの書き込みに失敗した場合は画面下部にエラーを表示します。設定が壊れている場合はWindows設定で起動し、その旨を表示します。

印影プレビューは白背景のままにし、テーマはPNGの画素や幾何コード、生成履歴、HMACの計算に使用しません。画像照合の入力画像もテーマによって変更しません。

## 実装

.NET 10 WPFに含まれるMicrosoftのFluentテーマを使用し、外部UIパッケージは追加していません。WindowのリソースでFluent標準スタイルを継承して余白を調整。サイドバーはTabControlのテンプレートを変更し、既存の選択・キーボード操作・画面インスタンスの保持を利用しています。選択状態は色と左端の印で示します。

テキスト・背景・罫線はFluentのDynamicResourceを使用。Windows設定の追従と高コントラストはFluent側の仕組みを利用します。ThemeMode APIには現行参照パックで実験的APIの注記があるため、依存箇所をAppearance.Applyに集約し、WPF0001の抑制をその箇所に限定しています。詳細は[Microsoftの実装ガイド](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md)参照。

## 確認範囲

通常版・軽量版のReleaseビルドとXAMLコンパイル、単一EXE内の現在のアセンブリ・同梱ランタイム・アイコンの整合を確認。使用したFluentリソースキーは同梱10.0.12のFluent DLLにも存在することを検査しました。

Windowsバッチに、サイドバーの配置、テーマ適用と保存・再読込、テーマ変更前後のプレビューPNG一致、照合ビューの保持、各テーマの画面キャプチャを追加しました。テストはコンパイル済みですが、WSLからWindowsプロセスを起動する経路がsocket failedで失敗するため、Windowsでの起動・描画・テーマ変更の実行結果は未確認です。DPI拡大やWindowsテーマ変更への追従も実機での追加確認が必要です。

外観だけの変更であり、40セルの冗長化試験は製品に統合していません。印影は引き続きwpf-v5-gap12です。

## タイトルバーの修正

タイトルバーは32 DIPに縮小し、メニュー文字は13 DIP・TextFillColorSecondaryBrush（本文より控えめ）にしました。トップレベルのメニューだけ独自のコンパクトなテンプレートを使い、Fluent標準テンプレート内の外側4 DIP・内側10 DIPの余白を持ち込まないようにしています。サブメニューは従来のFluent表示です。

前版は48 DIPの枠とOS任せのキャプション描画を使っていましたが、ユーザー環境で右上のボタンが見えない不具合がありました。現在はWPFで最小化・最大化／復元・閉じるを明示的に描きます。3つのボタンは各46×32 DIP、アイコンはフォント依存の文字ではなくベクター線です。最大化中は復元アイコンと説明に変わります。各ボタンにはキーボードフォーカス表示と読み上げ名を付けています。

WindowChromeのCaptionHeight=32、GlassFrameThickness=0、UseAeroCaptionButtons=false。ウィンドウ移動・空白のダブルクリック・右クリック・リサイズはWindowChrome、各ボタンの操作はSystemCommandsでOSへ渡します。右上の最大化領域はWM_NCHITTESTでHTMAXBUTTONとして通知し、Windows 11のスナップメニューに対応する構成です。非クライアント領域の押下はマウスをキャプチャし、ボタン内で離した場合にのみ最大化／復元します。ボタン外で離すと実行しません。

Windowsバッチには通常表示・最大化時のHTCAPTION/HTCLIENT/HTMAXBUTTON、ボタンの寸法・形状・描画画素の検査を追加しました。前版の「設定とヒットテストが正しければ見える」という検査不足を補います。新しいWindowsテストはコンパイル済みですが、実機接続が利用できず実行は未確認です。実際の押下、Altキーのメニュー操作、スナップ表示、DPIの違うモニター間の移動も実機確認が残ります。

参照：[Microsoftのカスタムタイトルバーとスナップの仕様](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-snap-layout-menu)、[FluentのMenuItemテンプレート](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/MenuItem.xaml)。
