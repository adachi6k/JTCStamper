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

public sealed class VerificationWindow : Window
{
    readonly VerificationService service;
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    readonly ListBox candidates = new();
    readonly ListBox regions = new() { MaxHeight = 85 };
    readonly Image preview = new() { Height = 250, Stretch = Stretch.Uniform, Cursor = Cursors.Cross };
    readonly Button show = new() { Content = "選択した生成履歴を開く", IsEnabled = false };
    readonly Button file = new() { Content = "ファイルを選択…" };
    readonly Button clipboard = new() { Content = "クリップボードの画像を照合" };
    BitmapSource? source;
    bool[]? mask;
    ImageRegion? selectedRegion;
    readonly CheckBox codeFilter = new() { Content = "12ビットの読取コードで絞る", IsChecked = true };
    IReadOnlyList<Generation> history = [];
    readonly List<StampTemplate> templates = [];
    string coverage = "";
    bool busy;
    Point? dragStart;
    public Guid? SelectedEventId { get; private set; }
    public VerificationWindow(Journal journal, string storageRoot)
    {
        service = new(journal);
        Title = "画像から印影を照合"; Width = 880; Height = 800; MinWidth = 640; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "照合先：" + storageRoot, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "画像内の赤い円形印を探して、履歴の印面と比較します。見つからない場合は画像上で印影をドラッグして囲んでください。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        file.Click += Open; buttons.Children.Add(file);
        clipboard.Click += FromClipboard; buttons.Children.Add(clipboard);
        top.Children.Add(codeFilter);
        codeFilter.Checked += Recompare;
        codeFilter.Unchecked += Recompare;
        top.Children.Add(preview); top.Children.Add(regions); top.Children.Add(result);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(show);
        bottom.Children.Add(new TextBlock { Text = "画像の類似候補は認証済みの一致ではありません。無断コピーの検出・偽造の断定はできません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        candidates.SelectionChanged += (_, _) => show.IsEnabled = !busy && candidates.SelectedItem is Candidate;
        show.Click += (_, _) => { if (candidates.SelectedItem is Candidate item) { SelectedEventId = item.Generation.EventId; DialogResult = true; } };
        regions.SelectionChanged += async (_, _) => { if (!busy && regions.SelectedItem is RegionItem item) await Compare(item.Region); };
        preview.MouseLeftButtonDown += (_, e) => { if (!busy) { dragStart = ImagePoint(e.GetPosition(preview)); if (dragStart is not null) preview.CaptureMouse(); } };
        preview.MouseLeftButtonUp += ManualRegion;
        panel.Children.Add(candidates);
    }
    sealed record Candidate(Generation Generation, string Evidence)
    {
        public override string ToString() => $"{Evidence} ／ {Generation.Stamp.Name} ／ 表示日付 {Generation.Stamp.DisplayDate:yyyy/MM/dd} ／ {Generation.Stamp.Bottom}\n生成 {Generation.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss} ／ {Generation.EventId}";
    }
    sealed record RegionItem(ImageRegion Region, string Label) { public override string ToString() => Label; }
    void SetBusy(bool value)
    {
        busy = value; codeFilter.IsEnabled = !value; file.IsEnabled = clipboard.IsEnabled = regions.IsEnabled = !value;
        show.IsEnabled = !value && candidates.SelectedItem is Candidate;
    }
    void Display(VerificationResult value)
    {
        candidates.ItemsSource = value.Matches.Select(x => new Candidate(x, "完全一致")).ToArray(); show.IsEnabled = false;
        result.Text = value.Label + "\n" + value.Explanation + (value.Matches.Count > 0 ? $"\n一致した履歴：{value.Matches.Count}件" : "");
    }
    void Failed(string reason) => Display(new(VerificationStatus.Indeterminate, reason, []));
    void Reset()
    {
        source = null; mask = null; selectedRegion = null; preview.Source = null; regions.ItemsSource = null; candidates.ItemsSource = null;
        templates.Clear(); history = []; coverage = ""; show.IsEnabled = false;
    }
    async void Open(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "画像・原本|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.jtc|画像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|原本|*.jtc", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        Reset(); SetBusy(true);
        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            bool original = Path.GetExtension(dialog.FileName).Equals(".jtc", StringComparison.OrdinalIgnoreCase);
            var bytes = ReadLimited(stream, original ? 1024 * 1024 : 32 * 1024 * 1024);
            if (original) Display(service.Original(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')));
            else await LoadImage(bytes, null);
        }
        catch (Exception ex) { Failed("ファイルを読み取れませんでした：" + ex.Message); }
        finally { SetBusy(false); }
    }
    async void FromClipboard(object sender, RoutedEventArgs e)
    {
        Reset(); SetBusy(true);
        try
        {
            var data = Clipboard.GetData("PNG");
            if (data is Stream stream) { if (stream.CanSeek) stream.Position = 0; await LoadImage(ReadLimited(stream, 32 * 1024 * 1024), null); }
            else if (data is byte[] bytes && bytes.Length <= 32 * 1024 * 1024) await LoadImage(bytes, null);
            else if (Clipboard.GetImage() is BitmapSource bitmap) await LoadImage(null, bitmap);
            else Failed("クリップボードに画像がありません。");
        }
        catch (Exception ex) { Failed("クリップボードを読み取れませんでした：" + ex.Message); }
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
        result.Text = "画像内の印影を探しています…";
        var found = await Task.Run(() => ImageSearch.Detect(mask, width, height));
        if (!IsVisible) return;
        result.Text = "生成履歴の印面を準備しています…";
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
                templates.Add(new(stamp, ImageSearch.Normalize(reference, rendered.PixelWidth, rendered.PixelHeight, bounds)));
            if (templates.Count % 8 == 0) { await Dispatcher.Yield(DispatcherPriority.Background); if (!IsVisible) return; }
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
        SetBusy(true); candidates.ItemsSource = null; Draw(region); result.Text = "選択した印影を履歴と比較しています…";
        try
        {
            var ink = ImageSearch.Normalize(mask, source.PixelWidth, source.PixelHeight, region);
            var crop = new CroppedBitmap(source, new Int32Rect(region.X, region.Y, region.Width, region.Height));
            var red = RingCode.RedStrength(ToPixels(crop), region.Width, region.Height);
            var localBox = new ImageRegion(0, 0, region.Width, region.Height);
            var reading = await Task.Run(() => RingCode.Decode(red, region.Width, region.Height, localBox));
            if (!IsVisible) return;
            bool filter = codeFilter.IsChecked == true && reading.Code.HasValue;
            var references = templates.Where(t => !filter || t.Stamp.GeometryCode == reading.Code).ToArray();
            var ranked = await Task.Run(() => ImageSearch.Rank(ink, references));
            if (!IsVisible) return;
            var rows = ranked.SelectMany(x => history.Where(g => g.Stamp == x.Stamp)
                .Select(g => new Candidate(g, $"類似度 {x.Score:0.000} ／ " + $"12ビット {RingCode.Label(g.Stamp.GeometryCode!.Value)}"))).ToArray();
            candidates.ItemsSource = rows;
            result.Text = "判定不能（画像の類似候補を検索）\n" + (rows.Length > 0
                ? $"似た印面の履歴が{rows.Length}件あります。文字と日付を確認してください。画像からイベントIDを復元した結果ではありません。"
                : "類似候補を見つけられませんでした。未記録・低解像度・色や形の変化などを区別できないため、偽造とは判断できません。") + "\n" + (reading.Code is int decoded
                    ? $"12ビット候補：{RingCode.Label(decoded)}。" + (filter ? "異なるコードの履歴を除外しています。" : "コードによる絞り込みは無効です。")
                    : "12ビット：判定不能。" + reading.Reason) + "\n" + coverage;
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
