using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JTCStamper.Core;

namespace JTCStamper.App;

// Runs inside the actual published EXE, on WPF's STA thread. Only disposable, marked roots are accepted.
internal static class SmokeTest
{
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    static int HitTest(MainWindow window, FrameworkElement element)
    {
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        int coordinates = (unchecked((ushort)(short)Math.Round(point.X))) | (unchecked((ushort)(short)Math.Round(point.Y)) << 16);
        return (int)SendMessage(new WindowInteropHelper(window).Handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates));
    }
    const string Marker = "JTCStamper isolated smoke data v1";
    sealed record CheckResult(string Name, string Status, string? Detail = null);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string root;
        string phase;
        bool clipboard;
        try
        {
            Require(args.Length is 5 or 6 && args[0] == "--smoke-test" && args[1] == "--test-root" &&
                args[3] == "--phase", "Invalid smoke-test arguments.");
            phase = args[4]; Require(phase is "seed" or "verify", "Invalid test phase.");
            clipboard = args.Length == 6;
            Require(!clipboard || args[5] == "--clipboard", "Invalid clipboard option.");
            root = Path.GetFullPath(args[2]);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Require(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase), "Test root must be inside TEMP.");
            Require(File.ReadAllText(Path.Combine(root, ".jtc-smoke-root")).Trim() == Marker, "Missing smoke-test marker.");
            // Reject junction/symlink traversal anywhere between the root and its volume root.
            for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
                Require((dir.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse points are not allowed in a test path.");
            if (phase == "seed") Require(!File.Exists(Path.Combine(root, "key.dpapi")) &&
                !Directory.Exists(Path.Combine(root, "journal")), "Seed requires an unused test root.");
        }
        catch { return 2; }

        var checks = new List<CheckResult>();
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new(name, "passed")); }
            catch (Exception ex) { checks.Add(new(name, "failed", ex.ToString())); }
        }
        async Task CheckAsync(string name, Func<Task> action)
        {
            try { await action(); checks.Add(new(name, "passed", null)); }
            catch (Exception ex) { checks.Add(new(name, "failed", ex.ToString())); }
        }
        var started = DateTimeOffset.UtcNow;
        var stamp = new Stamp("JTC", new DateOnly(2100, 1, 1), "(印)", RingCode.Renderer, 0);
        byte[]? png = null;
        try
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(root);
                window.Icon = App.LoadWindowIcon();
                var rendered = new TaskCompletionSource();
                window.ContentRendered += (_, _) => rendered.TrySetResult();
                window.Show();
                Require(await Task.WhenAny(rendered.Task, Task.Delay(15000)) == rendered.Task, "Window render timeout.");
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Require(window.IsVisible && window.ActualWidth > 0, "Window not visible.");
                Require(((Button)window.FindName("CopyButton")).IsEnabled, "Journal initialization failed: " +
                    ((TextBlock)window.FindName("Status")).Text);
                Require(((Image)window.FindName("Preview")).Source is not null, "Initial preview is empty.");
                Require(((TextBox)window.FindName("NameInput")).Text == "JTC" &&
                    ((TextBox)window.FindName("BottomInput")).Text == "(印)", "Unexpected initial values.");
                var tabs = (TabControl)window.FindName("MainTabs");
                var createTab = (TabItem)window.FindName("CreateTab");
                var verifyTab = (TabItem)window.FindName("VerificationTab");
                var historyTab = (TabItem)window.FindName("HistoryTab");
                var host = (ContentControl)window.FindName("VerificationHost");
                var view = host.Content;
                Require(view is VerificationView && verifyTab.IsEnabled, "Verification tab is not initialized.");
                var note = (TextBox)window.FindName("NoteInput");
                note.Text = "tab-state-check";
                tabs.SelectedItem = verifyTab;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                tabs.SelectedItem = historyTab;
                tabs.SelectedItem = createTab;
                tabs.SelectedItem = verifyTab;
                Require(ReferenceEquals(view, host.Content), "Verification view was recreated on tab switch.");
                Require(note.Text == "tab-state-check" && ((TextBox)window.FindName("NameInput")).Text == "JTC", "Tab switch lost input state.");
                Require(Window.GetWindow((VerificationView)view!) == window, "Verification is not hosted in the main window.");
                note.Clear(); tabs.SelectedItem = createTab;
                checks.Add(new("main-tabs-preserve-view-and-inputs", "passed"));
                Require(tabs.TabStripPlacement == Dock.Left, "Navigation is not in the left sidebar.");
                var chrome = WindowChrome.GetWindowChrome(window);
                Require(chrome is not null && !chrome.UseAeroCaptionButtons && chrome.CaptionHeight == 32, "Compact caption configuration missing.");
                foreach (var buttonName in new[] { "MinimizeButton", "MaximizeButton", "CloseButton" })
                {
                    var button = (Button)window.FindName(buttonName);
                    Require(button.IsVisible && button.ActualWidth >= 40 && button.ActualHeight == 32, $"Missing/incorrect caption button: {buttonName}");
                    Require(button.Content is System.Windows.Shapes.Path { Data: not null }, $"Caption glyph missing: {buttonName}");
                    var glyphVisual = new DrawingVisual();
                    using (var dc = glyphVisual.RenderOpen()) dc.DrawRectangle(new VisualBrush(button), null, new Rect(0, 0, 46, 32));
                    var glyphCapture = new RenderTargetBitmap(46, 32, 96, 96, PixelFormats.Pbgra32); glyphCapture.Render(glyphVisual);
                    var glyphPixels = new int[46 * 32]; glyphCapture.CopyPixels(glyphPixels, 46 * 4, 0);
                    Require(Enumerable.Range(10, 12).SelectMany(y => Enumerable.Range(17, 12).Select(x => glyphPixels[y * 46 + x])).Distinct().Count() > 1,
                        $"Caption glyph was not painted: {buttonName}");
                }
                Require(HitTest(window, (Button)window.FindName("MaximizeButton")) == 9, "Maximize must be HTMAXBUTTON for Snap.");
                var dragArea = (FrameworkElement)window.FindName("TitleDragArea");
                Require(dragArea.ActualWidth >= 80 && HitTest(window, dragArea) == 2, "Blank title region is not draggable HTCAPTION.");
                var titleMenu = (Menu)window.FindName("TitleMenu");
                Require(WindowChrome.GetIsHitTestVisibleInChrome(titleMenu) && HitTest(window, titleMenu) == 1, "Title menu is not clickable HTCLIENT.");
                window.WindowState = WindowState.Maximized;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Require(HitTest(window, dragArea) == 2 && HitTest(window, titleMenu) == 1 && HitTest(window, (Button)window.FindName("MaximizeButton")) == 9, "Maximized title/menu/button hit testing failed.");
                window.WindowState = WindowState.Normal;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                checks.Add(new("integrated-title-menu-native-hit-testing", "passed"));
                var imageBeforeTheme = StampRenderer.Png((BitmapSource)((Image)window.FindName("Preview")).Source);
                Color? lightText = null;
                foreach (var (name, mode) in new[] { ("LightThemeItem", AppearanceMode.Light), ("DarkThemeItem", AppearanceMode.Dark), ("SystemThemeItem", AppearanceMode.System) })
                {
                    ((MenuItem)window.FindName(name)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Require(Appearance.Current == mode && Appearance.Load(root) == mode, "Theme did not apply/persist.");
                    var textBrush = (SolidColorBrush)window.FindResource("TextFillColorPrimaryBrush");
                    if (mode == AppearanceMode.Light) lightText = textBrush.Color;
                    if (mode == AppearanceMode.Dark && !SystemParameters.HighContrast) Require(textBrush.Color != lightText, "Dark mode did not change text colors.");
                    Require(imageBeforeTheme.SequenceEqual(StampRenderer.Png((BitmapSource)((Image)window.FindName("Preview")).Source)), "Theme changed stamp pixels.");
                    Require(ReferenceEquals(view, host.Content), "Theme change recreated verification view.");
                    var themeCapture = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    themeCapture.Render(window);
                    File.WriteAllBytes(Path.Combine(root, $"window-{mode}-{phase}.png"), StampRenderer.Png(themeCapture));
                }
                checks.Add(new("sidebar-and-theme-persistence-with-unchanged-stamp", "passed"));
                var dateInput = (DatePicker)window.FindName("DateInput");
                var dateButton = (Button)window.FindName("DateModeButton");
                var dateLabel = (TextBlock)window.FindName("DateModeLabel");
                Require(!dateInput.IsEnabled && dateLabel.Text == "当日", "Default date is not locked to today.");
                dateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(dateInput.IsEnabled && dateLabel.Text == "指定日", "Explicit date button failed.");
                dateInput.SelectedDate = new DateTime(2100, 1, 1);
                dateInput.IsDropDownOpen = false;
                dateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!dateInput.IsEnabled && dateInput.SelectedDate == DateTime.Today && dateLabel.Text == "当日",
                    "Return to today failed.");
                checks.Add(new("today-default-and-explicit-date-button", "passed"));
                var capture = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                    (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                capture.Render(window);
                File.WriteAllBytes(Path.Combine(root, $"window-{phase}.png"), StampRenderer.Png(capture));
                checks.Add(new("real-window-startup-preview-and-icon", "passed"));
            }
            catch (Exception ex) { checks.Add(new("real-window-startup-preview-and-icon", "failed", ex.ToString())); }
            finally { window?.Close(); }

            Check("unsaved-settings-and-per-event-note-drafts", () =>
            {
                var folder = Path.Combine(root, "draft-ui-" + Guid.NewGuid().ToString("N"));
                var key = KeyStore.Load(folder);
                try
                {
                    using var data = new Journal(Path.Combine(folder, "journal"), key);
                    var copy = new CopyService(data, new FakeClipboard());
                    copy.GenerateCodedAndCopy(stamp, coded => StampRenderer.Png(StampRenderer.Render(coded)));
                    copy.GenerateCodedAndCopy(stamp, coded => StampRenderer.Png(StampRenderer.Render(coded)));
                }
                finally { CryptographicOperations.ZeroMemory(key); }
                var draftWindow = new MainWindow(folder);
                var name = (TextBox)draftWindow.FindName("NameInput");
                string originalName = name.Text;
                try
                {
                    Require(!draftWindow.HasUnsavedSettings && !draftWindow.HasUnsavedNotes, "Initial state incorrectly dirty.");
                    name.Text = "changed"; Require(draftWindow.HasUnsavedSettings, "Edited setting was not detected.");
                    name.Text = originalName; Require(!draftWindow.HasUnsavedSettings, "Reverted setting remains dirty.");
                    var list = (ListBox)draftWindow.FindName("History");
                    var note = (TextBox)draftWindow.FindName("NoteInput");
                    list.SelectedIndex = 0; note.Text = "note for first event";
                    list.SelectedIndex = 1; Require(note.Text == "", "Draft leaked to a different event.");
                    note.Text = "note for second event";
                    list.SelectedIndex = 0; Require(note.Text == "note for first event", "First draft was lost.");
                    note.Clear(); list.SelectedIndex = 1;
                    Require(note.Text == "note for second event" && draftWindow.HasUnsavedNotes, "Second draft was lost.");
                }
                finally { name.Text = originalName; draftWindow.ClearNoteDrafts(); draftWindow.Close(); }
            });

            Check("render-png-date-and-transparent-margin", () =>
            {
                var bitmap = StampRenderer.Render(stamp);
                Require(bitmap.PixelWidth == 384 && bitmap.PixelHeight == 384, "Unexpected PNG size.");
                png = StampRenderer.Png(bitmap);
                var pixels = new byte[384 * 384 * 4]; bitmap.CopyPixels(pixels, 384 * 4, 0);
                Require(pixels[3] == 0 && pixels.Where((_, i) => i % 4 == 3).Any(x => x != 0), "Unexpected transparency.");
                var sameShortYear = StampRenderer.Png(StampRenderer.Render(stamp with { DisplayDate = new DateOnly(1900, 1, 1) }));
                Require(png.SequenceEqual(sameShortYear), "Two-digit year rendering mismatch.");
                var changedDate = StampRenderer.Png(StampRenderer.Render(stamp with { DisplayDate = new DateOnly(2100, 1, 2) }));
                Require(!png.SequenceEqual(changedDate), "Date change did not change image.");
                File.WriteAllBytes(Path.Combine(root, $"stamp-{phase}.png"), png);
            });

            Check("plain-rendering-and-stored-history-image", () =>
            {
                var plain = stamp with { Renderer = RingCode.PlainRenderer, GeometryCode = null };
                var image = StampRenderer.Render(plain);
                var first = StampRenderer.Png(image);
                Require(first.SequenceEqual(StampRenderer.Png(StampRenderer.Render(plain))), "Plain image must be deterministic.");
                Require(!first.SequenceEqual(StampRenderer.Png(StampRenderer.Render(stamp))), "Plain rendering still looks coded.");
                var pixels = new byte[384 * 384 * 4]; image.CopyPixels(pixels, 384 * 4, 0);
                // Every circle cell must retain opaque red stroke; no hidden gaps.
                foreach (int cell in Enumerable.Range(0, RingCode.CellCount))
                {
                    double angle = RingCode.CellAngle(cell) * Math.PI / 180;
                    int x = (int)Math.Round((48 + 43 * Math.Cos(angle)) * 4);
                    int y = (int)Math.Round((48 + 43 * Math.Sin(angle)) * 4);
                    Require(pixels[(y * 384 + x) * 4 + 3] > 150, "Plain ring contains a code gap.");
                }
                var g = new Generation(Guid.NewGuid(), DateTimeOffset.UtcNow, plain,
                    Convert.ToHexString(SHA256.HashData(first)), Convert.ToBase64String(first));
                Require(HistoryImage.Load(g).Image is not null, "Stored history image cannot be displayed.");
                Require(HistoryImage.Load(g with { PngBase64 = null }).Image is not null, "Legacy hash-verified reconstruction failed.");
                Require(HistoryImage.Load(g with { PngBase64 = null, PngSha256 = new string('0', 64) }).Image is null, "Mismatched reconstruction was shown.");
            });

            Check("image-search-rendered-document-and-jpeg", () =>
            {
                bool[] Mask(BitmapSource image)
                {
                    var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
                    converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
                    return ImageSearch.RedMask(pixels, image.PixelWidth, image.PixelHeight);
                }
                var reference = StampRenderer.Render(stamp);
                var referenceMask = Mask(reference);
                var bounds = ImageSearch.Bounds(referenceMask, 384, 384)!;
                var template = new StampTemplate(stamp, ImageSearch.Normalize(referenceMask, 384, 384, bounds));
                foreach (int angle in new[] { 0, 4 })
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 480));
                        dc.PushTransform(new RotateTransform(angle, 240, 190));
                        dc.DrawImage(reference, new Rect(192, 142, 96, 96));
                        dc.Pop();
                    }
                    var page = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32); page.Render(visual);
                    var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(page));
                    using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
                    var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    var mask = Mask(decoded); var found = ImageSearch.Detect(mask, 640, 480);
                    Require(found.Count == 1, "Expected one embedded rendered stamp.");
                    var ranked = ImageSearch.Rank(ImageSearch.Normalize(mask, 640, 480, found[0]), [template]);
                    Require(ranked.Count == 1 && ranked[0].Stamp == stamp, "Rendered stamp candidate missing.");
                }
            });

            Check("same-stamp-font-change-keeps-old-jpeg-candidates", () =>
            {
                var plain = stamp with { Name = "SAMPLE", Bottom = "CHECK", Renderer = RingCode.PlainRenderer, GeometryCode = null };
                var firstFont = new Typeface("Arial"); var secondFont = new Typeface("Times New Roman");
                Require(firstFont.TryGetGlyphTypeface(out _) && secondFont.TryGetGlyphTypeface(out _), "Fixture fonts unavailable.");
                var oldImage = StampRenderer.Render(plain, firstFont);
                var oldPng = StampRenderer.Png(oldImage);
                var newPng = StampRenderer.Png(StampRenderer.Render(plain, secondFont));
                Require(!oldPng.SequenceEqual(newPng), "Font variants must have different PNGs.");
                var key = RandomNumberGenerator.GetBytes(32);
                try
                {
                    using var historyStore = new Journal(Path.Combine(root, "font-history-" + Guid.NewGuid().ToString("N")), key);
                    var copy = new CopyService(historyStore, new FakeClipboard());
                    var first = copy.GenerateCodedAndCopy(plain, _ => oldPng);
                    var duplicate = copy.GenerateCodedAndCopy(plain, _ => oldPng);
                    var latest = copy.GenerateCodedAndCopy(plain, _ => newPng);
                    var service = new VerificationService(historyStore);
                    var history = service.ReadGenerations();
                    // This is the pre-fix grouping: only the new font survives.
                    Require(history.Reverse().DistinctBy(g => g.Stamp).Single().EventId == latest, "Legacy reproduction changed.");
                    bool[] Normalize(BitmapSource image)
                    {
                        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
                        var mask = ImageSearch.RedMask(pixels, image.PixelWidth, image.PixelHeight);
                        var bounds = ImageSearch.Bounds(mask, image.PixelWidth, image.PixelHeight) ?? throw new InvalidDataException("No stamp bounds.");
                        return ImageSearch.Normalize(mask, image.PixelWidth, image.PixelHeight, bounds);
                    }
                    var references = HistoryReferences.LatestImages(history).Select(g =>
                    {
                        using var bytes = new MemoryStream(VerificationService.StoredPng(g)!);
                        var bitmap = BitmapFrame.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        return new StampTemplate(g.Stamp, Normalize(bitmap), g.PngSha256);
                    }).ToArray();
                    Require(references.Length == 2, "A historical font image was dropped.");
                    using var jpeg = new MemoryStream();
                    var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(oldImage)); encoder.Save(jpeg);
                    Require(service.Image(jpeg.ToArray()).Status == VerificationStatus.NoRecord, "JPEG unexpectedly used exact-match path.");
                    jpeg.Position = 0;
                    var input = BitmapFrame.Create(jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var ranked = ImageSearch.Rank(Normalize(input), references);
                    var oldCandidate = ranked.Single(x => x.PngSha256 == history[0].PngSha256);
                    var matched = HistoryReferences.Matching(history, oldCandidate).Select(x => x.EventId).ToHashSet();
                    Require(matched.SetEquals(new[] { first, duplicate }) && !matched.Contains(latest), "Candidate bound to the wrong image events.");
                    Require(service.Image(oldPng).Matches.Count == 2, "Exact duplicates changed.");
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            });

            Check("windows-delete-failure-leaves-valid-prefix-and-can-retry", () =>
            {
                var key = RandomNumberGenerator.GetBytes(32);
                try
                {
                    var folder = Path.Combine(root, "delete-failure-" + Guid.NewGuid().ToString("N"));
                    using var journal = new Journal(folder, key);
                    for (int i = 0; i < 3; i++) journal.Append("Test", Guid.NewGuid(), new { Index = i });
                    using (var locked = new FileStream(Path.Combine(folder, "000000000002.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bool rejected = false;
                        try { journal.DeleteAllHistory(); } catch (IOException) { rejected = true; }
                        Require(rejected && journal.Read().Count == 2, "Partial deletion did not preserve a valid prefix.");
                    }
                    journal.DeleteAllHistory(); Require(journal.Read().Count == 0, "Delete retry failed.");
                    journal.Append("Test", Guid.NewGuid(), new { Restart = true });
                    Require(journal.Read().Single().Entry.Sequence == 1, "Generation after deletion failed.");
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            });

            Check("missing-and-damaged-key-never-recreated", () =>
            {
                var folder = Path.Combine(root, "missing-key-" + Guid.NewGuid().ToString("N"));
                var key = KeyStore.Load(folder);
                try
                {
                    using (var journal = new Journal(Path.Combine(folder, "journal"), key)) journal.Append("Test", Guid.NewGuid(), new { });
                    var path = Path.Combine(folder, "key.dpapi"); File.Delete(path);
                    bool rejected = false;
                    try { KeyStore.Load(folder); } catch (InvalidDataException) { rejected = true; }
                    Require(rejected && !File.Exists(path), "Missing key was silently replaced.");
                    File.WriteAllBytes(path, [1, 2, 3]); rejected = false;
                    try { KeyStore.Load(folder); } catch (CryptographicException) { rejected = true; }
                    Require(rejected && File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3 }), "Damaged key was replaced.");
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            });

            Check("ring12-rendered-decoding-and-controls", () =>
            {
                var observations = new List<object>(); int correct = 0, wrong = 0, unreadable = 0, flatClassified = 0, mimicClassified = 0;
                foreach (string name in new[] { "JTC", "田中" }) foreach (int code in new[] { 0, 1, 0x555, 0xAAA, 0xABC, 4095 })
                foreach (int diameter in new[] { 88, 176 }) foreach (int condition in new[] { 0, 1, 2 })
                {
                    var specimen = stamp with { Name = name, Renderer = RingCode.Renderer, GeometryCode = code };
                    var reading = ReadGeometry(specimen, diameter, condition == 2 ? 4 : 0, condition != 0);
                    if (reading.Code == code) correct++; else if (reading.Code is null) unreadable++; else wrong++;
                    observations.Add(new { Kind = "encoded", Name = name, Expected = (int?)code, Diameter = diameter, Condition = condition, Reading = reading });
                }
                foreach (int condition in new[] { 0, 1, 2 })
                {
                    var flat = ReadGeometry(stamp with { GeometryCode = null }, 88, condition == 2 ? 4 : 0, condition != 0);
                    if (flat.Code is not null) flatClassified++;
                    observations.Add(new { Kind = "unencoded-control", Expected = (int?)null, Reading = flat });
                    var mimic = ReadGeometry(stamp with { Name = "模倣", Renderer = RingCode.Renderer, GeometryCode = 0xABC }, 88, condition == 2 ? 4 : 0, condition != 0);
                    if (mimic.Code is not null) mimicClassified++;
                    observations.Add(new { Kind = "imitation-with-valid-code", Expected = (int?)null, Reading = mimic });
                }
                File.WriteAllText(Path.Combine(root, $"ring12-{phase}.json"), JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1, EncodedTotal = 72, Correct = correct, WrongCode = wrong, Unreadable = unreadable,
                    UnencodedControls = 3, UnencodedClassified = flatClassified, ImitationControls = 3, ImitationClassified = mimicClassified,
                    AuthenticationFalseAcceptanceRate = "NOT EVALUATED: decoding is candidate retrieval, not authentication", Observations = observations
                }, new JsonSerializerOptions { WriteIndented = true }));
                Require(correct == 72 && wrong == 0 && unreadable == 0 && flatClassified == 0,
                    $"Ring12: correct={correct}/72 wrong={wrong} unreadable={unreadable} flatClassified={flatClassified}/3; see ring12 report.");
            });

            if (phase == "seed")
            {
                Check("settings-file-save-reload-and-validation", () =>
                {
                    var settings = new StampSettings(1, stamp.Name, stamp.DisplayDate, stamp.Bottom);
                    var path = Path.Combine(root, "test.jtcstamp"); settings.Save(path);
                    Require(StampSettings.Load(path) == settings, "Settings roundtrip failed.");
                    bool rejected = false;
                    try { (settings with { Name = "" }).Save(path); } catch (InvalidDataException) { rejected = true; }
                    Require(rejected && StampSettings.Load(path) == settings, "Invalid save changed settings.");
                });
                Check("dpapi-journal-annotation-reopen", () =>
                {
                    var key = KeyStore.Load(root);
                    try
                    {
                        var reopenedKey = KeyStore.Load(root);
                        try { Require(key.SequenceEqual(reopenedKey), "DPAPI key roundtrip failed."); }
                        finally { CryptographicOperations.ZeroMemory(reopenedKey); }
                        Guid id;
                        using (var journal = new Journal(Path.Combine(root, "journal"), key))
                        {
                            Require(png is not null, "Rendering prerequisite failed.");
                            id = new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, coded => StampRenderer.Png(StampRenderer.Render(coded)));
                            var original = journal.Read()[0].Signed;
                            journal.Annotate(id, "Isolated smoke test; no paste observed");
                            Require(journal.Read()[0].Signed == original, "Annotation modified generation.");
                        }
                        using var reopened = new Journal(Path.Combine(root, "journal"), key);
                        var records = reopened.Read();
                        Require(records.Count == 4 && records.All(x => x.Entry.EventId == id), "Reopen mismatch.");
                        var generation = JsonSerializer.Deserialize<Generation>(records[0].Entry.Payload)!;
                        Require(generation.Stamp.GeometryCode == RingCode.ForEvent(id), "Geometry code was not preserved in journal.");
                        Require(generation.Stamp.DisplayDate == stamp.DisplayDate &&
                            Math.Abs((DateTimeOffset.UtcNow - generation.CreatedUtc).TotalMinutes) < 5,
                            "Displayed date and creation timestamp were not preserved separately.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                Check("same-user-backup-restores-images-notes-and-original", () =>
                {
                    var key = KeyStore.Load(root);
                    try
                    {
                        using var sourceJournal = new Journal(Path.Combine(root, "journal"), key);
                        var records = sourceJournal.Read().Select(x => x.Signed).ToArray();
                        var backup = Path.Combine(root, "test.jtcbackup");
                        HistoryBackup.Save(backup, root, sourceJournal);
                        var target = Path.Combine(root, "restored-backup");
                        HistoryBackup.Restore(backup, target);
                        var restoredKey = KeyStore.Load(target);
                        try
                        {
                            Require(key.SequenceEqual(restoredKey), "Restored DPAPI key differs.");
                            using var restored = new Journal(Path.Combine(target, "journal"), restoredKey);
                            Require(restored.Read().Select(x => x.Signed).SequenceEqual(records), "Restored history differs.");
                            var service = new VerificationService(restored);
                            Require(service.Original(JsonSerializer.Serialize(records[0])).Status == VerificationStatus.Match, "Restored original authentication failed.");
                            var generation = service.ReadGenerations().Single();
                            Require(service.Image(VerificationService.StoredPng(generation)!).Status == VerificationStatus.Match, "Stored image was not restored.");
                        }
                        finally { CryptographicOperations.ZeroMemory(restoredKey); }
                        bool refused = false;
                        try { HistoryBackup.Restore(backup, target); } catch (IOException) { refused = true; }
                        Require(refused, "Restore overwrote an existing destination.");
                        var bytes = File.ReadAllBytes(backup); bytes[^1] ^= 1;
                        var damaged = Path.Combine(root, "damaged.jtcbackup"); File.WriteAllBytes(damaged, bytes);
                        var failedTarget = Path.Combine(root, "must-not-exist");
                        refused = false;
                        try { HistoryBackup.Restore(damaged, failedTarget); } catch (InvalidDataException) { refused = true; }
                        Require(refused && !Directory.Exists(failedTarget), "Damaged backup produced a restored directory.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                Check("persistence-failure-prevents-clipboard", () =>
                {
                    var key = RandomNumberGenerator.GetBytes(32);
                    try
                    {
                        var folder = Path.Combine(root, "failure-case");
                        using var journal = new Journal(folder, key);
                        Directory.CreateDirectory(Path.Combine(folder, "000000000001.json"));
                        var spy = new FakeClipboard(); bool rejected = false;
                        try { new CopyService(journal, spy).GenerateCodedAndCopy(stamp, _ => png ?? [1]); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
                        Require(rejected && spy.Calls == 0, "Clipboard called despite failed persistence.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                Check("hmac-tamper-detection", () =>
                {
                    var key = RandomNumberGenerator.GetBytes(32);
                    try
                    {
                        var folder = Path.Combine(root, "tamper-case");
                        using var journal = new Journal(folder, key);
                        journal.Append("Generated", Guid.NewGuid(), new { Text = "before" });
                        var path = Directory.GetFiles(folder, "*.json").Single();
                        File.WriteAllText(path, File.ReadAllText(path).Replace("before", "after"));
                        bool rejected = false;
                        try { journal.Read(); } catch (InvalidDataException) { rejected = true; }
                        Require(rejected, "Tampered HMAC accepted.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                Check("clipboard-data-object-keeps-png-stream-readable", () =>
                {
                    Require(png is not null, "Rendering prerequisite failed.");
                    var data = WindowsClipboard.CreateData(png!);
                    var stream = data.GetData("PNG") as Stream;
                    Require(stream is not null && stream.CanRead, "PNG stream was closed before its data object was consumed.");
                    using var copy = new MemoryStream(); stream!.CopyTo(copy);
                    Require(copy.ToArray().SequenceEqual(png!), "Data object changed PNG bytes.");
                    Require(data.GetData(DataFormats.Bitmap) is BitmapSource, "Bitmap fallback missing from data object.");
                });
                if (clipboard)
                {
                    await CheckAsync("windows-clipboard-png-roundtrip", async () =>
                    {
                        Require(Environment.UserInteractive, "Interactive Windows session required.");
                        Require(png is not null, "Rendering prerequisite failed.");
                        for (int attempt = 0; attempt < 8; attempt++)
                        {
                            new WindowsClipboard().Copy(png!);
                            await VerifyClipboard(png!, NativeClipboard.SequenceNumber);
                        }
                    });
                }
                else checks.Add(new("windows-clipboard-png-roundtrip", "skipped", "Not requested; use -IncludeClipboard."));
                Check("write-restart-baseline", () => File.WriteAllText(Path.Combine(root, "baseline.json"),
                    JsonSerializer.Serialize(Snapshot(root))));
            }
            else
            {
                Check("restart-and-executable-replacement-data-preserved", () =>
                {
                    var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "baseline.json")))!;
                    var actual = Snapshot(root);
                    Require(expected.Count == actual.Count && expected.All(x => actual.TryGetValue(x.Key, out var hash) && hash == x.Value),
                        "Persisted data changed after restart/replacement.");
                    Require(StampSettings.Load(Path.Combine(root, "test.jtcstamp")).DisplayDate.Year == 2100, "Settings date lost.");
                    var key = KeyStore.Load(root);
                    try
                    {
                        using var journal = new Journal(Path.Combine(root, "journal"), key);
                        Require(journal.Read().Count == 4, "History or annotation lost.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                Check("restored-backup-survives-process-and-edition-change", () =>
                {
                    var target = Path.Combine(root, "restored-backup");
                    var key = KeyStore.Load(target);
                    try
                    {
                        using var restored = new Journal(Path.Combine(target, "journal"), key);
                        Require(restored.Read().Count == 4, "Restored annotation or history missing.");
                        var service = new VerificationService(restored);
                        var generation = service.ReadGenerations().Single();
                        Require(service.Image(VerificationService.StoredPng(generation)!).Status == VerificationStatus.Match, "Restored image authentication failed after restart.");
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                if (clipboard) await CheckAsync("clipboard-survives-process-exit", async () =>
                {
                    Require(png is not null, "Rendering prerequisite failed.");
                    await VerifyClipboard(png!);
                });
                else checks.Add(new("clipboard-survives-process-exit", "skipped", "Not requested; use -IncludeClipboard."));
            }
        }
        catch (Exception ex) { checks.Add(new("fatal", "failed", ex.ToString())); }
        var passed = checks.All(x => x.Status != "failed");
        var report = new { SchemaVersion = 1, Phase = phase, StartedUtc = started, FinishedUtc = DateTimeOffset.UtcNow,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Passed = passed, ClipboardRequested = clipboard, Checks = checks };
        try { File.WriteAllText(Path.Combine(root, $"report-{phase}.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
        catch { return 3; }
        return passed ? 0 : 1;
    }

    static async Task VerifyClipboard(byte[] png, uint? sequence = null)
    {
        var data = await ClipboardImages.ReadAsync(requireBitmap: true);
        Require(data?.Png is not null, "PNG clipboard payload missing.");
        var bytes = data!.Png!;
        Require(bytes.SequenceEqual(png), $"PNG clipboard bytes changed: expected {png.Length}, actual {bytes.Length}, clipboard sequence changed {sequence.HasValue && sequence != NativeClipboard.SequenceNumber}.");
        var image = data.Bitmap;
        Require(image is not null && image.PixelWidth == 384 && image.PixelHeight == 384,
            "Bitmap fallback missing: " + (image is null ? "null" : $"{image.PixelWidth}x{image.PixelHeight}"));
    }

    static RingReading ReadGeometry(Stamp specimen, int diameter, int angle, bool jpeg)
    {
        BitmapSource reference;
        if (specimen.GeometryCode.HasValue) reference = StampRenderer.Render(specimen);
        else
        {
            // Negative test image, not an application rendering format.
            var control = new DrawingVisual();
            using (var dc = control.RenderOpen())
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(195, 32, 40)), 1.1);
                dc.DrawEllipse(null, pen, new Point(48, 48), 43, 43);
                dc.DrawLine(pen, new Point(8, 33), new Point(88, 33));
                dc.DrawLine(pen, new Point(8, 63), new Point(88, 63));
            }
            var image = new RenderTargetBitmap(384, 384, 384, 384, PixelFormats.Pbgra32);
            image.Render(control); reference = image;
        }
        double size = diameter * 96.0 / 87.1;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 300, 300));
            dc.PushTransform(new RotateTransform(angle, 150, 150));
            dc.DrawImage(reference, new Rect(150 - size / 2, 150 - size / 2, size, size));
            dc.Pop();
        }
        var page = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32); page.Render(visual);
        BitmapSource decoded = page;
        using var stream = new MemoryStream();
        if (jpeg)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 }; encoder.Frames.Add(BitmapFrame.Create(page));
            encoder.Save(stream); stream.Position = 0;
            decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[300 * 300 * 4]; converted.CopyPixels(pixels, 300 * 4, 0);
        var regions = ImageSearch.Detect(ImageSearch.RedMask(pixels, 300, 300), 300, 300);
        if (regions.Count != 1) return new(null, null, "Detection failed");
        var red = RingCode.RedStrength(pixels, 300, 300);
        return RingCode.Decode(red, 300, 300, regions[0]);
    }

    static Dictionary<string, string> Snapshot(string root)
    {
        var paths = new[] { Path.Combine(root, "key.dpapi"), Path.Combine(root, "test.jtcstamp") }
            .Concat(Directory.GetFiles(Path.Combine(root, "journal"), "*.json"));
        return paths.ToDictionary(x => Path.GetRelativePath(root, x), x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
    }
    sealed class FakeClipboard : IClipboard
    {
        public int Calls { get; private set; }
        public void Copy(byte[] png) => Calls++;
    }
}
