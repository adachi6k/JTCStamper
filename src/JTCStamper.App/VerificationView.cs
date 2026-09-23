using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeights.SemiBold, Text = "画像または原本を選んでください" };
    readonly TextBlock meaning = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock nextStep = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock technical = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock candidateHeading = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), Text = "生成履歴" };
    readonly TextBlock scoreHelp = new() { Text = VerificationMessages.ScoreHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6), Visibility = Visibility.Collapsed };
    readonly ScrollViewer resultScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ListBox candidates = new();
    readonly ListBox regions = new() { MaxHeight = 85 };
    readonly Image preview = new() { Height = 250, Stretch = Stretch.Uniform, Cursor = Cursors.Cross };
    readonly Button show = new() { Content = "選択した生成履歴を開く", IsEnabled = false };
    readonly Button file = new() { Content = "ファイルを選択…" };
    readonly Button clipboard = new() { Content = "クリップボードの画像を照合" };
    BitmapSource? source;
    bool[]? mask;
    ImageRegion? selectedRegion;
    readonly CheckBox codeFilter = new() { Content = "読めた印影コードで候補を絞る", IsChecked = true, IsEnabled = false };
    IReadOnlyList<Generation> history = [];
    readonly List<StampTemplate> templates = [];
    readonly Dictionary<Stamp, string> referenceHashes = [];
    string coverage = "";
    bool busy;
    Point? dragStart;
    public event Action<Guid>? HistoryRequested;
    public bool IsBusy => busy;
    bool disposed;
    byte[]? lastBytes;
    BitmapSource? lastBitmap;
    bool lastOriginal;
    readonly Button refresh = new() { Content = "再照合", IsEnabled = false };
    public void CancelPending() { disposed = true; preview.ReleaseMouseCapture(); }

    public VerificationView(Journal journal, string storageRoot)
    {
        service = new(journal);
        var panel = new DockPanel { Margin = new Thickness(12) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "印影を照合", Style = (Style)FindResource("PageHeading") });
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        file.Click += Open; buttons.Children.Add(file);
        clipboard.Click += FromClipboard; buttons.Children.Add(clipboard);
        refresh.Click += RefreshInput; buttons.Children.Add(refresh);
        codeFilter.ToolTip = "印影コードを読めた場合だけ、同じコードの履歴に絞ります。外すと、コードが異なる形のみの候補も表示します。";
        top.Children.Add(codeFilter);
        codeFilter.Checked += Recompare; codeFilter.Unchecked += Recompare;
        var footer = new TextBlock { Text = "この照合は、保存した生成履歴との対応を調べるものです。利用の許可や貼付完了を証明するものではありません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        panel.Children.Add(body);
        var imagePanel = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = imagePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        imagePanel.Children.Add(new TextBlock { Text = "照合する印影", FontWeight = FontWeights.SemiBold });
        imagePanel.Children.Add(preview);
        imagePanel.Children.Add(new TextBlock { Text = "印影が複数ある場合は一覧から選択してください。見つからない場合は、画像上で円全体をドラッグして囲めます。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        imagePanel.Children.Add(regions);
        imagePanel.Children.Add(new Expander { Header = "履歴の保存先", Margin = new Thickness(0, 12, 0, 0),
            Content = new TextBlock { Text = storageRoot, TextWrapping = TextWrapping.Wrap } });
        Grid.SetColumn(resultScroll, 2); body.Children.Add(resultScroll);
        var results = new StackPanel(); resultScroll.Content = results;
        results.Children.Add(title);
        void Section(string label, TextBlock text)
        {
            results.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3) });
            results.Children.Add(text);
        }
        Section("確認できたこと", result);
        Section("この結果の意味", meaning);
        Section("次に確認すること", nextStep);
        result.Text = "このPCに保存した生成履歴と照合します。";
        nextStep.Text = "［ファイルを選択…］または［クリップボードの画像を照合］を押してください。";
        results.Children.Add(candidateHeading); results.Children.Add(scoreHelp);
        candidates.MinHeight = 70; candidates.MaxHeight = 240;
        ScrollViewer.SetHorizontalScrollBarVisibility(candidates, ScrollBarVisibility.Disabled);
        candidates.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        candidates.ItemTemplate = new DataTemplate { VisualTree = factory };
        results.Children.Add(candidates); results.Children.Add(show);
        results.Children.Add(new Expander { Header = "読取・検索の詳細", Content = technical, Margin = new Thickness(0, 8, 0, 0) });
        results.Children.Add(new Expander { Header = "対応スコアの配点と限界", Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = VerificationMessages.MethodHelp, TextWrapping = TextWrapping.Wrap } });
        candidates.SelectionChanged += (_, _) => show.IsEnabled = !busy && candidates.SelectedItem is Candidate;
        show.Click += (_, _) => { if (candidates.SelectedItem is Candidate item) HistoryRequested?.Invoke(item.Generation.EventId); };
        regions.SelectionChanged += async (_, _) => { if (!busy && regions.SelectedItem is RegionItem item) await Compare(item.Region); };
        preview.MouseLeftButtonDown += (_, e) => { if (!busy) { dragStart = ImagePoint(e.GetPosition(preview)); if (dragStart is not null) preview.CaptureMouse(); } };
        preview.MouseLeftButtonUp += ManualRegion;
    }

    sealed record Candidate(Generation Generation, string Evidence)
    {
        public override string ToString() => $"{Evidence}\n氏名：{Generation.Stamp.Name} ／ 表示日付：{Generation.Stamp.DisplayDate:yyyy/MM/dd} ／ 下段：{Generation.Stamp.Bottom}\n生成日時：{Generation.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}\n記録ID：{Generation.EventId}";
    }
    sealed record RegionItem(ImageRegion Region, string Label) { public override string ToString() => Label; }
    void SetBusy(bool value)
    {
        busy = value; codeFilter.IsEnabled = !value && source is not null; file.IsEnabled = clipboard.IsEnabled = regions.IsEnabled = !value;
        refresh.IsEnabled = !value && (lastBytes is not null || lastBitmap is not null);
        show.IsEnabled = !value && candidates.SelectedItem is Candidate;
    }
    void Present(VerificationMessage message, string details = "")
    {
        title.Text = message.Title; result.Text = message.Findings;
        meaning.Text = message.Meaning; nextStep.Text = message.NextStep;
        technical.Text = details; resultScroll.ScrollToTop();
    }
    void Progress(string text)
    {
        title.Text = "照合中…"; result.Text = text; meaning.Text = nextStep.Text = technical.Text = "";
        scoreHelp.Visibility = Visibility.Collapsed; candidateHeading.Text = "生成履歴";
    }
    void Display(VerificationResult value)
    {
        candidates.ItemsSource = value.Matches.Select(x => new Candidate(x, lastOriginal ? "原本の認証情報・内容が一致" : "PNGファイル全体が一致")).ToArray();
        show.IsEnabled = false; scoreHelp.Visibility = Visibility.Collapsed;
        candidateHeading.Text = value.Status == VerificationStatus.Match ? $"一致した生成履歴：{value.Matches.Count}件" : "生成履歴";
        Present(VerificationMessages.Exact(value, lastOriginal), value.Explanation);
    }
    void Failed(string reason)
    {
        candidates.ItemsSource = null; show.IsEnabled = false; scoreHelp.Visibility = Visibility.Collapsed;
        candidateHeading.Text = "生成履歴";
        Present(VerificationMessages.Unavailable(reason,
            "印影の範囲、元の画像、履歴の保存先を確認してください。下の詳細に理由が表示されている場合は、その内容も確認してください。"), reason);
    }
    void Reset()
    {
        source = null; mask = null; selectedRegion = null; preview.Source = null; regions.ItemsSource = null; candidates.ItemsSource = null;
        templates.Clear(); referenceHashes.Clear(); history = []; coverage = ""; show.IsEnabled = false;
    }
    async void Open(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "画像・原本|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.jtc|画像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|原本|*.jtc", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        lastBytes = null; lastBitmap = null; lastOriginal = false;
        Reset(); SetBusy(true);
        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            bool original = Path.GetExtension(dialog.FileName).Equals(".jtc", StringComparison.OrdinalIgnoreCase);
            var bytes = ReadLimited(stream, original ? 1024 * 1024 : 32 * 1024 * 1024);
            lastBytes = bytes; lastOriginal = original;
            if (original) Display(service.Original(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')));
            else await LoadImage(bytes, null);
        }
        catch (Exception ex) { Failed("ファイルを読み取れませんでした：" + ex.Message); }
        finally { SetBusy(false); }
    }
    async void FromClipboard(object sender, RoutedEventArgs e)
    {
        lastBytes = null; lastBitmap = null; lastOriginal = false;
        Reset(); SetBusy(true);
        try
        {
            var data = Clipboard.GetData("PNG");
            if (data is Stream stream) { if (stream.CanSeek) stream.Position = 0; lastBytes = ReadLimited(stream, 32 * 1024 * 1024); await LoadImage(lastBytes, null); }
            else if (data is byte[] bytes && bytes.Length <= 32 * 1024 * 1024) { lastBytes = bytes; await LoadImage(bytes, null); }
            else if (Clipboard.GetImage() is BitmapSource bitmap) { lastBitmap = bitmap; await LoadImage(null, bitmap); }
            else Failed("クリップボードに画像がありません。");
        }
        catch (Exception ex) { Failed("クリップボードを読み取れませんでした：" + ex.Message); }
        finally { SetBusy(false); }
    }
    async void RefreshInput(object sender, RoutedEventArgs e)
    {
        if (busy || disposed || (lastBytes is null && lastBitmap is null)) return;
        Reset(); SetBusy(true);
        try
        {
            if (lastOriginal) Display(service.Original(new UTF8Encoding(false, true).GetString(lastBytes!).TrimStart('\uFEFF')));
            else await LoadImage(lastBytes, lastBitmap);
        }
        catch (Exception ex) { Failed("再照合できませんでした：" + ex.Message); }
        finally { SetBusy(false); }
    }
    async Task LoadImage(byte[]? bytes, BitmapSource? bitmap)
    {
        if (bytes is not null)
        {
            var exact = service.Image(bytes);
            if (exact.Status != VerificationStatus.NoRecord) { Display(exact); return; }
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            if (decoder.Frames.Count != 1) { Failed("複数ページの画像は、照合したいページをPNGやJPEGにして選択してください。"); return; }
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 12_000_000) { Failed("画像は1200万画素以内にしてください。"); return; }
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
            converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, frame.PixelWidth * 4);
        }
        if (bitmap is null || (long)bitmap.PixelWidth * bitmap.PixelHeight > 12_000_000) { Failed("画像は1200万画素以内にしてください。"); return; }
        // Keep source pixels: shrinking a whole document can erase the 12-bit marks.
        source = bitmap;
        source.Freeze(); Draw(null);
        var width = source.PixelWidth; var height = source.PixelHeight;
        mask = ToMask(source);
        Progress("画像内の印影を探しています…");
        var found = await Task.Run(() => ImageSearch.Detect(mask, width, height));
        if (disposed) return;
        Progress("生成履歴の印面を準備しています…");
        history = service.ReadGenerations(); // Revalidates all HMACs before creating reference images.
        var groups = history.Reverse().Select(x => x.Stamp).Distinct().ToArray();
        int skipped = 0;
        foreach (var stamp in groups.Take(300))
        {
            var rendered = StampRenderer.Render(stamp);
            string hash = Convert.ToHexString(SHA256.HashData(StampRenderer.Png(rendered)));
            // Font/renderer changes must not silently become historical image evidence.
            if (!history.Any(x => x.Stamp == stamp && x.PngSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
            var reference = ToMask(rendered);
            if (ImageSearch.Bounds(reference, rendered.PixelWidth, rendered.PixelHeight) is ImageRegion bounds)
                { templates.Add(new(stamp, ImageSearch.Normalize(reference, rendered.PixelWidth, rendered.PixelHeight, bounds))); referenceHashes[stamp] = hash; }
            if (templates.Count % 8 == 0) { await Dispatcher.Yield(DispatcherPriority.Background); if (disposed) return; }
        }
        coverage = $"比較対象：印面{templates.Count}種類。" + (groups.Length > 300 ? "直近300種類に限定しています。" : "") +
            (skipped > 0 ? $"過去画像を再現できない{skipped}種類は比較対象外です。" : "");
        regions.ItemsSource = found.Take(30).Select((x, i) => new RegionItem(x, $"検出した印影 {i + 1}：({x.X}, {x.Y}) {x.Width}×{x.Height}")).ToArray();
        if (found.Count > 30) coverage += "検出領域は先頭30件を表示しています。";
        if (found.Count == 0) { Failed("赤い円形の印影を自動検出できませんでした。画像上で印影を囲んで範囲を指定できます。\n" + coverage); return; }
        regions.SelectedIndex = 0;
        await Compare(found[0]);
    }
    async Task Compare(ImageRegion region)
    {
        if (source is null || mask is null) return;
        selectedRegion = region;
        SetBusy(true); candidates.ItemsSource = null; Draw(region); Progress("選択した印影を履歴と比較しています…");
        try
        {
            history = service.ReadGenerations(); // Recheck integrity when selecting another region or changing the filter.
            var ink = ImageSearch.Normalize(mask, source.PixelWidth, source.PixelHeight, region);
            var crop = new CroppedBitmap(source, new Int32Rect(region.X, region.Y, region.Width, region.Height));
            var red = RingCode.RedStrength(ToPixels(crop), region.Width, region.Height);
            var localBox = new ImageRegion(0, 0, region.Width, region.Height);
            var reading = await Task.Run(() => RingCode.Decode(red, region.Width, region.Height, localBox));
            if (disposed) return;
            bool filter = codeFilter.IsChecked == true && reading.Code.HasValue;
            var references = templates.Where(t => !filter || t.Stamp.GeometryCode == reading.Code).ToArray();
            var ranked = await Task.Run(() => ImageSearch.Rank(ink, references, reading.Code));
            if (disposed) return;
            var rows = ranked.SelectMany(x => history.Where(g => g.Stamp == x.Stamp &&
                    referenceHashes.TryGetValue(x.Stamp, out var hash) && g.PngSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                .Select(g => new Candidate(g, VerificationMessages.ShapeEvidence(reading.Code, g.Stamp.GeometryCode!.Value, x.Text)))).ToArray();
            candidates.ItemsSource = rows;
            int codeMatches = reading.Code.HasValue ? rows.Count(x => x.Generation.Stamp.GeometryCode == reading.Code) : 0;
            int codeHistory = reading.Code.HasValue ? history.Count(x => x.Stamp.GeometryCode == reading.Code) : 0;
            candidateHeading.Text = $"生成履歴候補：{rows.Length}件";
            scoreHelp.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var message = VerificationMessages.Image(new(reading.Code, rows.Length, codeMatches, codeHistory, templates.Count, filter));
            Present(message, (reading.Code is int decoded ? $"読取コード：{RingCode.Label(decoded)}（12ビット）\n" : "コード未読取：" + reading.Reason + "\n") +
                (filter ? "同じコードの履歴に絞っています。" : "コードによる絞り込みは適用していません。") + "\n" + coverage +
                "\n従来の内側全体の形比較が0.72以上の候補から、対応スコア順で上位8印面の記録を表示します。0.72は候補抽出用で、対応スコアの基準ではありません。文字・日付は目視で確認してください。");
        }
        catch (Exception ex) { Failed("画像を比較できませんでした：" + ex.Message); }
        finally { SetBusy(false); }
    }
    async void Recompare(object sender, RoutedEventArgs e)
    {
        if (!busy && selectedRegion is ImageRegion region) await Compare(region);
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
    static byte[] ReadLimited(Stream stream, int limit)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("ファイルがサイズ上限を超えています。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
