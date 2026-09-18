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
                if (clipboard)
                {
                    Check("windows-clipboard-png-roundtrip", () =>
                    {
                        Require(Environment.UserInteractive, "Interactive Windows session required.");
                        Require(png is not null, "Rendering prerequisite failed.");
                        new WindowsClipboard().Copy(png!);
                        VerifyClipboard(png!);
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
                if (clipboard) Check("clipboard-survives-process-exit", () =>
                {
                    Require(png is not null, "Rendering prerequisite failed.");
                    VerifyClipboard(png!);
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

    static void VerifyClipboard(byte[] png)
    {
                        Require(Clipboard.ContainsData("PNG") && Clipboard.ContainsImage(), "Clipboard formats missing.");
                        var payload = Clipboard.GetData("PNG");
                        var bytes = payload switch
                        {
                            MemoryStream stream => stream.ToArray(),
                            byte[] array => array,
                            _ => throw new InvalidDataException("Unexpected PNG clipboard payload.")
                        };
                        Require(bytes.SequenceEqual(png!), "PNG clipboard bytes changed.");
                        var image = Clipboard.GetImage();
                        Require(image is not null && image.PixelWidth == 384 && image.PixelHeight == 384, "Bitmap fallback missing.");
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
