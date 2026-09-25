using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JTCStamper.Core;
using Microsoft.Win32;

namespace JTCStamper.App;

public sealed class VerificationView : UserControl
{
    readonly VerificationService service;
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeights.SemiBold, Text = "画像または原本を選んでください" };
    readonly TextBlock meaning = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock nextStep = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock technical = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock candidateHeading = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4), Text = "生成履歴" };
    readonly TextBlock scoreHelp = new() { Text = VerificationMessages.ScoreHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6), Visibility = Visibility.Collapsed };
    readonly ScrollViewer resultScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    Candidate[] candidateRows = [];
    int candidateIndex = -1;
    Candidate? SelectedCandidate => candidateIndex >= 0 && candidateIndex < candidateRows.Length ? candidateRows[candidateIndex] : null;
    readonly TextBlock scoreValue = new() { FontSize = 44, FontWeight = FontWeights.SemiBold, Text = "—" };
    readonly TextBlock scoreVerdict = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Text = "画像を選択してください" };
    readonly Border scoreCard = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 8) };
    readonly TextBlock candidateText = new() { FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock candidateDetails = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock candidatePosition = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    readonly Button previousCandidate = new() { Content = "← 前の候補", IsEnabled = false };
    readonly Button nextCandidate = new() { Content = "次の候補 →", IsEnabled = false };
    readonly Image candidateImage = new() { Height = 92, Stretch = Stretch.Uniform };
    readonly Border candidateImagePanel = new() { Background = Brushes.White, Padding = new Thickness(4), Visibility = Visibility.Collapsed };
    readonly TextBlock candidateImageDescription = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    readonly TextBlock candidateCaution = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    readonly StackPanel candidateNavigation = new() { Orientation = Orientation.Horizontal };
    readonly Button previousRegion = new() { Content = "←", Width = 38, ToolTip = "前の印影", IsEnabled = false };
    readonly Button nextRegion = new() { Content = "→", Width = 38, ToolTip = "次の印影", IsEnabled = false };
    readonly TextBlock regionPosition = new() { Text = "検出した印影なし", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    readonly ListBox regions = new() { MaxHeight = 70 };
    readonly Image preview = new() { Height = 200, Stretch = Stretch.Uniform, Cursor = Cursors.Cross };
    readonly Button show = new() { Content = "選択した生成履歴を開く", IsEnabled = false };
    readonly Button file = new() { Content = "ファイルを選択…" };
    readonly Button clipboard = new() { Content = "クリップボードの画像を照合" };
    BitmapSource? source;
    bool[]? mask;
    ImageRegion? selectedRegion;
    readonly CheckBox codeFilter = new() { Content = "読めた印影コードで候補を絞る", IsChecked = true, IsEnabled = false };
    IReadOnlyList<Generation> history = [];
    readonly List<StampTemplate> templates = [];
    string coverage = "";
    bool busy;
    CancellationTokenSource? operationCancellation;
    readonly Button cancel = new() { Content = "照合を中止", IsEnabled = false };
    internal void CancelCurrent() { operationCancellation?.Cancel(); }
    internal string ResultTitle => title.Text;
    internal int CandidateCount => candidateRows.Length;
    internal int DetectedRegionCount => regions.Items.Count;
    internal string Findings => result.Text;
    internal string SearchCoverage => coverage;
    async Task RunVerificationAsync(Func<CancellationToken, Task> action)
    {
        if (busy || disposed) return;
        using var cancellation = new CancellationTokenSource();
        operationCancellation = cancellation; SetBusy(true);
        try { await action(cancellation.Token); cancellation.Token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!disposed)
            {
                Reset();
                Present(new(VerificationMessageKind.Unavailable, "照合を中止しました", "結果は確定していません。", "履歴の一致・不一致は判断していません。", "［再照合］または別の画像を選択してください。"));
            }
        }
        catch (Exception ex) { if (!disposed) Failed("照合できませんでした：" + ex.Message); }
        finally
        {
            if (ReferenceEquals(operationCancellation, cancellation)) operationCancellation = null;
            if (!disposed) SetBusy(false);
        }
    }
    internal Task VerifyBytesAsync(byte[] bytes, bool original = false) =>
        RunVerificationAsync(token => LoadInput(bytes, original, token));
    async Task LoadInput(byte[] bytes, bool original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lastBytes = null; lastBitmap = null; lastOriginal = original;
        Reset();
        if (bytes.Length > (original ? 1024 * 1024 : 32 * 1024 * 1024)) throw new InvalidDataException("ファイルがサイズ上限を超えています。");
        lastBytes = bytes;
        Progress("履歴と入力を確認しています…");
        if (original)
        {
            var value = await Task.Run(() => service.Original(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), token), token);
            token.ThrowIfCancellationRequested(); Display(value);
        }
        else await LoadImage(bytes, null, token);
    }
    Point? dragStart;
    public event Action<Guid>? HistoryRequested;
    public bool IsBusy => busy;
    bool disposed;
    byte[]? lastBytes;
    BitmapSource? lastBitmap;
    bool lastOriginal;
    readonly Button refresh = new() { Content = "再照合", IsEnabled = false };
    public void CancelPending() { disposed = true; operationCancellation?.Cancel(); preview.ReleaseMouseCapture(); }

    public VerificationView(Journal journal, string storageRoot)
    {
        service = new(journal);
        var panel = new DockPanel { Margin = new Thickness(6) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "印影を照合", Style = (Style)FindResource("PageHeading") });
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        file.Click += Open; buttons.Children.Add(file);
        clipboard.Click += FromClipboard; buttons.Children.Add(clipboard);
        refresh.Click += RefreshInput; buttons.Children.Add(refresh);
        cancel.Click += (_, _) => CancelCurrent(); buttons.Children.Add(cancel);
        codeFilter.ToolTip = "印影コードを読めた場合だけ、同じコードの履歴に絞ります。外すと、コードが異なる形のみの候補も表示します。";
        top.Children.Add(codeFilter);
        codeFilter.Checked += Recompare; codeFilter.Unchecked += Recompare;
        var footer = new TextBlock { Text = "この照合は、保存した生成履歴との対応を調べるものです。利用の許可や貼付完了を証明するものではありません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        panel.Children.Add(body);
        var imagePanel = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = imagePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        imagePanel.Children.Add(new TextBlock { Text = "照合する印影", FontWeight = FontWeights.SemiBold });
        imagePanel.Children.Add(preview);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        navigation.Children.Add(previousRegion); navigation.Children.Add(regionPosition); navigation.Children.Add(nextRegion);
        imagePanel.Children.Add(navigation);
        System.Windows.Automation.AutomationProperties.SetName(previousRegion, "前の印影");
        System.Windows.Automation.AutomationProperties.SetName(nextRegion, "次の印影");
        previousRegion.Click += (_, _) => MoveRegion(-1);
        nextRegion.Click += (_, _) => MoveRegion(1);
        imagePanel.Children.Add(new TextBlock { Text = "印影が複数ある場合は一覧から選択してください。見つからない場合は、画像上で円全体をドラッグして囲めます。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        imagePanel.Children.Add(regions);
        Grid.SetColumn(resultScroll, 2); body.Children.Add(resultScroll);
        candidateNavigation.Visibility = Visibility.Collapsed;
        var results = new StackPanel(); results.SetValue(TextElement.FontSizeProperty, 16.0); resultScroll.Content = results;
        void Section(string label, TextBlock text)
        {
            results.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3) });
            results.Children.Add(text);
        }
        result.Text = "このPCに保存した生成履歴と照合します。";
        nextStep.Text = "［ファイルを選択…］または［クリップボードの画像を照合］を押してください。";
        results.Children.Add(candidateHeading);
        var scoreContent = new StackPanel();
        scoreContent.Children.Add(new TextBlock { Text = "履歴との対応", FontSize = 16 });
        scoreContent.Children.Add(scoreValue); scoreContent.Children.Add(scoreVerdict);
        scoreCard.Child = scoreContent; scoreCard.Background = Brushes.LightGray; scoreValue.Foreground = scoreVerdict.Foreground = Brushes.Black;
        results.Children.Add(scoreCard);
        results.Children.Add(candidateCaution);
        var candidateInfo = new Grid { Margin = new Thickness(0, 6, 0, 4) };
        candidateInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        candidateInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var imageInfo = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        candidateImagePanel.Child = candidateImage;
        imageInfo.Children.Add(candidateImagePanel); imageInfo.Children.Add(candidateImageDescription);
        candidateInfo.Children.Add(imageInfo); Grid.SetColumn(candidateText, 1); candidateInfo.Children.Add(candidateText);
        results.Children.Add(candidateInfo);
        candidateNavigation.Children.Add(previousCandidate); candidateNavigation.Children.Add(candidatePosition); candidateNavigation.Children.Add(nextCandidate);
        results.Children.Add(candidateNavigation); results.Children.Add(show);
        previousCandidate.Click += (_, _) => SelectCandidate(candidateIndex - 1);
        nextCandidate.Click += (_, _) => SelectCandidate(candidateIndex + 1);
        results.Children.Add(title);
        Section("確認できたこと", result);
        Section("次に確認すること", nextStep);
        var detailPanel = new StackPanel();
        detailPanel.Children.Add(candidateDetails); detailPanel.Children.Add(scoreHelp);
        detailPanel.Children.Add(meaning); detailPanel.Children.Add(technical);
        detailPanel.Children.Add(new TextBlock { Text = "照合に使う生成履歴の保存先：" + storageRoot, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        results.Children.Add(new Expander { Header = "内訳・読取・検索の詳細", Content = detailPanel, Margin = new Thickness(0, 8, 0, 0) });
        results.Children.Add(new Expander { Header = "対応スコアの配点と限界", Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = VerificationMessages.MethodHelp, TextWrapping = TextWrapping.Wrap } });
        show.Click += (_, _) => { if (SelectedCandidate is Candidate item) HistoryRequested?.Invoke(item.Generation.EventId); };
        regions.SelectionChanged += async (_, _) => { UpdateRegionNavigation(); if (!busy && regions.SelectedItem is RegionItem item) await Compare(item.Region); };
        preview.MouseLeftButtonDown += (_, e) => { if (!busy) { dragStart = ImagePoint(e.GetPosition(preview)); if (dragStart is not null) preview.CaptureMouse(); } };
        preview.MouseLeftButtonUp += ManualRegion;
    }

    sealed record Candidate(Generation Generation, string Evidence, double? Score = null);
    void SetCandidates(Candidate[] rows)
    {
        candidateRows = rows; candidateIndex = rows.Length > 0 ? 0 : -1;
        RenderCandidate();
    }
    void SelectCandidate(int index)
    {
        if (busy || index < 0 || index >= candidateRows.Length) return;
        candidateIndex = index; RenderCandidate();
    }
    void PaintIndicator(ResultIndicator indicator)
    {
        scoreValue.Text = indicator.Value; scoreVerdict.Text = indicator.Label;
        scoreCard.Background = indicator.Tone switch
        {
            IndicatorTone.Success => new SolidColorBrush(Color.FromRgb(16, 100, 55)),
            IndicatorTone.NoCandidate => new SolidColorBrush(Color.FromRgb(165, 32, 35)),
            _ => new SolidColorBrush(Color.FromRgb(255, 221, 112))
        };
        var foreground = indicator.Tone == IndicatorTone.Caution ? Brushes.Black : Brushes.White;
        ((StackPanel)scoreCard.Child).SetValue(TextElement.ForegroundProperty, foreground);
        foreach (TextBlock text in ((StackPanel)scoreCard.Child).Children) text.Foreground = foreground;
    }
    void UpdateCandidateNavigation()
    {
        show.IsEnabled = !busy && SelectedCandidate is not null;
        previousCandidate.IsEnabled = !busy && candidateIndex > 0;
        nextCandidate.IsEnabled = !busy && candidateIndex >= 0 && candidateIndex + 1 < candidateRows.Length;
        candidateNavigation.Visibility = candidateRows.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    void RenderCandidate()
    {
        UpdateCandidateNavigation();
        candidateImage.Source = null; candidateImagePanel.Visibility = Visibility.Collapsed;
        candidateImageDescription.Text = candidateText.Text = candidateDetails.Text = candidateCaution.Text = "";
        if (SelectedCandidate is not Candidate item) return;
        candidatePosition.Text = $"{candidateIndex + 1} / {candidateRows.Length}";
        PaintIndicator(item.Score is double score ? ResultIndicator.Candidate(score) : ResultIndicator.Exact());
        candidateCaution.Text = item.Score.HasValue ? "暫定スコアです。1.000でも真正性の証明ではありません。" : "保存された生成記録と一致しました。貼付完了の証明ではありません。";
        var g = item.Generation;
        candidateText.Text = $"表示日付：{g.Stamp.DisplayDate:yyyy/MM/dd}\n上段文字：{g.Stamp.Name}\n下段文字：{g.Stamp.Bottom}\n生成：{g.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}";
        candidateDetails.Text = item.Evidence + $"\n記録ID：{g.EventId}\n";
        try
        {
            var image = HistoryImage.Load(g);
            candidateImage.Source = image.Image; candidateImageDescription.Text = image.Description;
            candidateImagePanel.Visibility = image.Image is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch { candidateImageDescription.Text = "履歴の画像を表示できません。"; }
    }
    sealed record RegionItem(ImageRegion Region, string Label) { public override string ToString() => Label; }
    void MoveRegion(int direction)
    {
        if (busy || regions.Items.Count == 0) return;
        int target = regions.SelectedIndex < 0 ? (direction > 0 ? 0 : regions.Items.Count - 1) : regions.SelectedIndex + direction;
        if (target < 0 || target >= regions.Items.Count) return;
        regions.SelectedIndex = target;
        regions.ScrollIntoView(regions.SelectedItem);
    }
    void UpdateRegionNavigation()
    {
        int index = regions.SelectedIndex, count = regions.Items.Count;
        regionPosition.Text = count == 0 ? "検出した印影なし" : index < 0 ? $"手動選択（検出 {count}件）" : $"印影 {index + 1} / {count}";
        previousRegion.IsEnabled = !busy && count > 0 && index != 0;
        nextRegion.IsEnabled = !busy && count > 0 && (index < count - 1);
    }
    void SetBusy(bool value)
    {
        busy = value; cancel.IsEnabled = value; UpdateRegionNavigation(); codeFilter.IsEnabled = !value && source is not null; file.IsEnabled = clipboard.IsEnabled = regions.IsEnabled = !value;
        refresh.IsEnabled = !value && (lastBytes is not null || lastBitmap is not null);
        UpdateCandidateNavigation();
    }
    void Present(VerificationMessage message, string details = "")
    {
        title.Text = message.Title; result.Text = message.Findings;
        meaning.Text = message.Meaning; nextStep.Text = message.NextStep;
        technical.Text = details; resultScroll.ScrollToTop();
        if (SelectedCandidate is null)
        {
            PaintIndicator(ResultIndicator.Empty(message.Kind == VerificationMessageKind.NoCandidate));
            candidateCaution.Text = message.Kind == VerificationMessageKind.NoCandidate ? "今回の検索範囲での結果です。偽造を意味しません。" : "未読取や比較不能を、履歴なしとは判断しません。";
        }
    }
    void Progress(string text)
    {
        scoreValue.Text = "…"; scoreVerdict.Text = "照合中"; scoreCard.Background = Brushes.LightGray; ((StackPanel)scoreCard.Child).SetValue(TextElement.ForegroundProperty, Brushes.Black); foreach (TextBlock block in ((StackPanel)scoreCard.Child).Children) block.Foreground = Brushes.Black;
        title.Text = "照合中…"; result.Text = text; meaning.Text = nextStep.Text = technical.Text = "";
        scoreHelp.Visibility = Visibility.Collapsed; candidateHeading.Text = "生成履歴";
    }
    void Display(VerificationResult value)
    {
        SetCandidates(value.Matches.Select(x => new Candidate(x, lastOriginal ? "原本の認証情報・内容が一致" : "PNGファイル全体が一致")).ToArray());
        show.IsEnabled = false; scoreHelp.Visibility = Visibility.Collapsed;
        candidateHeading.Text = value.Status == VerificationStatus.Match ? $"一致した生成履歴：{value.Matches.Count}件" : "生成履歴";
        Present(VerificationMessages.Exact(value, lastOriginal), value.Explanation);
    }
    void Failed(string reason)
    {
        SetCandidates([]); show.IsEnabled = false; scoreHelp.Visibility = Visibility.Collapsed;
        candidateHeading.Text = "生成履歴";
        Present(VerificationMessages.Unavailable(reason,
            "印影の範囲、元の画像、履歴の保存先を確認してください。下の詳細に理由が表示されている場合は、その内容も確認してください。"), reason);
    }
    void Reset()
    {
        source = null; mask = null; selectedRegion = null; preview.Source = null; regions.ItemsSource = null; SetCandidates([]);
        templates.Clear(); history = []; coverage = ""; show.IsEnabled = false;
        candidateHeading.Text = "生成履歴"; scoreHelp.Visibility = Visibility.Collapsed;
    }
    async void Open(object sender, RoutedEventArgs e)
    {
        if (busy || disposed) return;
        var dialog = new OpenFileDialog { Filter = "画像・原本|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.jtc|画像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|原本|*.jtc", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await RunVerificationAsync(async token =>
        {
            lastBytes = null; lastBitmap = null; lastOriginal = false; Reset();
            Progress("ファイルを読み取っています…");
            bool original = Path.GetExtension(dialog.FileName).Equals(".jtc", StringComparison.OrdinalIgnoreCase);
            var bytes = await Task.Run(() =>
            {
                using var stream = File.OpenRead(dialog.FileName);
                return ReadLimited(stream, original ? 1024 * 1024 : 32 * 1024 * 1024, token);
            }, token);
            await LoadInput(bytes, original, token);
        });
    }
    async void FromClipboard(object sender, RoutedEventArgs e)
    {
        await RunVerificationAsync(async token =>
        {
            lastBytes = null; lastBitmap = null; lastOriginal = false;
            Reset(); Progress("クリップボードの画像を読み取っています…");
            var data = await ClipboardImages.ReadAsync();
            token.ThrowIfCancellationRequested();
            if (data?.Png is byte[] bytes) await LoadInput(bytes, false, token);
            else if (data?.Bitmap is BitmapSource bitmap) { lastBitmap = bitmap; await LoadImage(null, bitmap, token); }
            else Failed("クリップボードに画像がありません。");
        });
    }
    async void RefreshInput(object sender, RoutedEventArgs e)
    {
        if (lastBytes is null && lastBitmap is null) return;
        await RunVerificationAsync(async token =>
        {
            if (lastBytes is not null) await LoadInput(lastBytes, lastOriginal, token);
            else { Reset(); await LoadImage(null, lastBitmap, token); }
        });
    }
    async Task LoadImage(byte[]? bytes, BitmapSource? bitmap, CancellationToken token)
    {
        if (bytes is not null)
        {
            var exact = await Task.Run(() => service.Image(bytes, token), token);
            token.ThrowIfCancellationRequested();
            if (exact.Status != VerificationStatus.NoRecord) { Display(exact); return; }
            bitmap = await Task.Run(() => DecodeInput(bytes), token);
            token.ThrowIfCancellationRequested();
        }
        if (bitmap is null || (long)bitmap.PixelWidth * bitmap.PixelHeight > 12_000_000) { Failed("画像は1200万画素以内にしてください。"); return; }
        // Keep source pixels: shrinking a whole document can erase the 12-bit marks.
        source = bitmap;
        source.Freeze(); Draw(null);
        var width = source.PixelWidth; var height = source.PixelHeight;
        mask = await Task.Run(() => ToMask(source), token);
        token.ThrowIfCancellationRequested();
        Progress("画像内の印影を探しています…");
        var found = await Task.Run(() => ImageSearch.Detect(mask, width, height, token), token);
        token.ThrowIfCancellationRequested();
        Progress("生成履歴の印面を準備しています…");
        history = await Task.Run(() => service.ReadGenerations(token), token);
        token.ThrowIfCancellationRequested();
        var groups = HistoryReferences.LatestImages(history, 301);
        int skipped = 0;
        foreach (var generation in groups.Take(300))
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            token.ThrowIfCancellationRequested();
            var stamp = generation.Stamp;
            var historical = HistoryImage.Load(generation);
            if (historical.Image is not BitmapSource rendered) { skipped++; continue; }
            string hash = generation.PngSha256;
            var reference = ToMask(rendered);
            if (ImageSearch.Bounds(reference, rendered.PixelWidth, rendered.PixelHeight) is ImageRegion bounds)
                { templates.Add(new(stamp, ImageSearch.Normalize(reference, rendered.PixelWidth, rendered.PixelHeight, bounds), hash)); }
        }
        coverage = $"比較対象：保存画像{templates.Count}種類。" + (groups.Length > 300 ? "直近300種類に限定しています。" : "") +
            (skipped > 0 ? $"過去画像を再現できない{skipped}種類は比較対象外です。" : "");
        regions.ItemsSource = found.Take(30).Select((x, i) => new RegionItem(x, $"検出した印影 {i + 1}：({x.X}, {x.Y}) {x.Width}×{x.Height}")).ToArray();
        if (found.Count > 30) coverage += "検出領域は先頭30件を表示しています。";
        if (found.Count == 0) { Failed("赤い円形の印影を自動検出できませんでした。画像上で印影を囲んで範囲を指定できます。\n" + coverage); return; }
        regions.SelectedIndex = 0;
        await CompareCore(found[0], token);
    }
    Task Compare(ImageRegion region) => RunVerificationAsync(token => CompareCore(region, token));
    async Task CompareCore(ImageRegion region, CancellationToken token)
    {
        if (source is null || mask is null) return;
        selectedRegion = region;
        SetCandidates([]); Draw(region); Progress("選択した印影を履歴と比較しています…");
        history = await Task.Run(() => service.ReadGenerations(token), token);
        token.ThrowIfCancellationRequested();
        var width = source.PixelWidth; var height = source.PixelHeight;
        var ink = await Task.Run(() => ImageSearch.Normalize(mask, width, height, region), token);
        token.ThrowIfCancellationRequested();
        var crop = new CroppedBitmap(source, new Int32Rect(region.X, region.Y, region.Width, region.Height));
        var red = RingCode.RedStrength(ToPixels(crop), region.Width, region.Height);
        var localBox = new ImageRegion(0, 0, region.Width, region.Height);
        var reading = await Task.Run(() => RingCode.Decode(red, region.Width, region.Height, localBox, token), token);
        token.ThrowIfCancellationRequested();
        bool filter = codeFilter.IsChecked == true && reading.Code.HasValue;
        var references = templates.Where(t => !filter || t.Stamp.GeometryCode == reading.Code).ToArray();
        var ranked = await Task.Run(() => ImageSearch.Rank(ink, references, reading.Code, token), token);
        token.ThrowIfCancellationRequested();
        var rows = ranked.SelectMany(x => HistoryReferences.Matching(history, x)
            .Select(g => new Candidate(g, VerificationMessages.ShapeEvidence(reading.Code, g.Stamp.GeometryCode, x.Text), CorrespondenceScore.Calculate(x.Text, reading.Code, g.Stamp.GeometryCode).Total))).ToArray();
        SetCandidates(rows);
        int codeMatches = reading.Code.HasValue ? rows.Count(x => x.Generation.Stamp.GeometryCode == reading.Code) : 0;
        int codeHistory = reading.Code.HasValue ? history.Count(x => x.Stamp.GeometryCode == reading.Code) : 0;
        candidateHeading.Text = $"生成履歴候補（スコア順）：{rows.Length}件";
        scoreHelp.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var message = history.Count == 0
            ? new VerificationMessage(VerificationMessageKind.NoCandidate, "照合対象の生成履歴がありません",
                "この保存先には、対応する形式の生成記録がありません。", "この保存先についての結果です。偽造という意味ではありません。",
                "生成したときの履歴の保存先を確認してください。")
            : VerificationMessages.Image(new(reading.Code, rows.Length, codeMatches, codeHistory, templates.Count, filter));
        Present(message, (reading.Code is int decoded ? $"読取コード：{RingCode.Label(decoded)}（12ビット）\n" : "コード未読取：" + reading.Reason + "\n") +
            (filter ? "同じコードの履歴に絞っています。" : "コードによる絞り込みは適用していません。") + "\n" + coverage +
            "\n従来の内側全体の形比較が0.72以上の候補から、対応スコア順で上位8画像の記録を表示します。0.72は候補抽出用で、対応スコアの基準ではありません。文字・日付は目視で確認してください。");
    }

    async void Recompare(object sender, RoutedEventArgs e)
    {
        if (!busy && selectedRegion is ImageRegion region) await Compare(region);
    }
    static BitmapSource DecodeInput(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) throw new InvalidDataException("複数ページの画像は、照合したいページをPNGやJPEGにして選択してください。");
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 12_000_000) throw new InvalidDataException("画像は1200万画素以内にしてください。");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
        converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        var bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, frame.PixelWidth * 4);
        bitmap.Freeze(); return bitmap;
    }
    static bool[] ToMask(BitmapSource bitmap)
    {
        return ImageSearch.RedMask(ToPixels(bitmap), bitmap.PixelWidth, bitmap.PixelHeight);
    }
    static byte[] ToPixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        converted.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }
    void Draw(ImageRegion? selected)
    {
        if (source is null) return;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            if (selected is not null) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, Math.Max(1, source.PixelWidth / 450.0)),
                new Rect(selected.X, selected.Y, selected.Width, selected.Height));
        }
        preview.Source = new DrawingImage(group);
    }
    Point? ImagePoint(Point point)
    {
        if (source is null || preview.ActualWidth <= 0 || preview.ActualHeight <= 0) return null;
        double scale = Math.Min(preview.ActualWidth / source.PixelWidth, preview.ActualHeight / source.PixelHeight);
        double x = (point.X - (preview.ActualWidth - source.PixelWidth * scale) / 2) / scale;
        double y = (point.Y - (preview.ActualHeight - source.PixelHeight * scale) / 2) / scale;
        return x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight ? null : new Point(x, y);
    }
    async void ManualRegion(object sender, MouseButtonEventArgs e)
    {
        var start = dragStart; dragStart = null; preview.ReleaseMouseCapture();
        var end = ImagePoint(e.GetPosition(preview));
        if (busy || start is null || end is null || source is null || mask is null) return;
        int x0 = (int)Math.Min(start.Value.X, end.Value.X), y0 = (int)Math.Min(start.Value.Y, end.Value.Y);
        int x1 = (int)Math.Max(start.Value.X, end.Value.X), y1 = (int)Math.Max(start.Value.Y, end.Value.Y);
        if (x1 - x0 < 20 || y1 - y0 < 20) return;
        int left = x1, right = x0, top = y1, bottom = y0;
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (mask[y * source.PixelWidth + x])
        { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right - left < 20 || bottom - top < 20) { Failed("指定範囲に十分な赤い印影がありません。"); return; }
        regions.SelectedIndex = -1;
        await Compare(new(left, top, right - left + 1, bottom - top + 1));
    }
    static byte[] ReadLimited(Stream stream, int limit, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (output.Length + count > limit) throw new InvalidDataException("ファイルがサイズ上限を超えています。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
