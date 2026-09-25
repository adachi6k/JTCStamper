using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using JTCStamper.App;
using JTCStamper.Core;

internal static class Program
{
    static readonly List<object> results = [];
    static string root = "";
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        root = Path.GetFullPath(args[0]);
        if (!File.Exists(Path.Combine(root, ".performance-root"))) return 2;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Load the product's actual style source, without scheduling its normal startup handler.
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("ApplicationXamlSource")!)
        {
            var source = XDocument.Load(stream); var ns = source.Root!.Name.Namespace;
            var dictionary = new XElement(ns + "ResourceDictionary", source.Root.Attributes().Where(x => x.IsNamespaceDeclaration),
                source.Root.Element(ns + "Application.Resources")!.Elements());
            app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        }
        int code = 0;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Run(); }
            catch (Exception ex) { code = 1; results.Add(new { Error = ex.ToString() }); }
            finally
            {
                Save(); app.Shutdown(code); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run(); return code;
    }
    static void Save() => File.WriteAllText(Path.Combine(root, "performance.json"), JsonSerializer.Serialize(new
    {
        Os = Environment.OSVersion.VersionString, Utc = DateTimeOffset.UtcNow,
        Product = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        WarmFileCache = true, SamplesPerCase = 1, Clipboard = "Fake sink; never modifies the user's clipboard", Results = results
    }, new JsonSerializerOptions { WriteIndented = true }));
    static async Task Run()
    {
        var fixtures = Enumerable.Range(0, 300).Select(i =>
        {
            var stamp = new Stamp($"TEST{i:000}", new DateOnly(2100, 1, 1).AddDays(i), "CHECK", RingCode.PlainRenderer);
            return (Stamp: stamp, Png: StampRenderer.Png(StampRenderer.Render(stamp)));
        }).ToArray();
        foreach (int count in new[] { 100, 1000, 10000 })
        {
            var folder = Path.Combine(root, count + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var key = KeyStore.Load(folder); var journalPath = Path.Combine(folder, "journal"); Directory.CreateDirectory(journalPath);
            Console.WriteLine($"Preparing {count} synthetic generations...");
            await Task.Run(() =>
            {
                string previous = "GENESIS"; long sequence = 0;
                void Append(string kind, Guid id, object payload)
                {
                    var entry = new Entry(1, ++sequence, previous, kind, id, DateTimeOffset.UnixEpoch, JsonSerializer.Serialize(payload));
                    var json = JsonSerializer.Serialize(entry);
                    var signed = new SignedEntry(json, Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json))));
                    File.WriteAllText(Path.Combine(journalPath, $"{sequence:D12}.json"), JsonSerializer.Serialize(signed)); previous = signed.Mac;
                }
                for (int i = 0; i < count; i++)
                {
                    var f = fixtures[i % fixtures.Length]; var id = Guid.NewGuid();
                    Append("Generated", id, new Generation(id, DateTimeOffset.UnixEpoch.AddSeconds(i), f.Stamp,
                        Convert.ToHexString(SHA256.HashData(f.Png)), Convert.ToBase64String(f.Png)));
                    Append("CopyRequested", id, new { Format = "PNG" });
                    Append("CopyCompleted", id, new { Format = "PNG", Meaning = "Synthetic fixture; paste unobserved" });
                }
            });
            Console.WriteLine($"Measuring {count}...");
            var watch = Stopwatch.StartNew();
            var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [folder], null)!;
            double constructor = watch.Elapsed.TotalMilliseconds;
            var rendered = new TaskCompletionSource(); window.ContentRendered += (_, _) => rendered.TrySetResult(); window.Show(); await rendered.Task;
            double startup = watch.Elapsed.TotalMilliseconds;
            if (!((Button)window.FindName("CopyButton")).IsEnabled) throw new InvalidDataException("Fixture journal was not accepted.");
            var history = (ListBox)window.FindName("History");
            if (history.Items.Count != count) throw new InvalidDataException("History count mismatch.");
            var uiClock = Stopwatch.StartNew(); double lastTick = 0, maxGap = 0; int uiTicks = 0;
            var heartbeat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
            heartbeat.Tick += (_, _) => { double now = uiClock.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - lastTick); lastTick = now; uiTicks++; };
            heartbeat.Start();
            watch.Restart(); history.SelectedIndex = 0; double selection = watch.Elapsed.TotalMilliseconds;
            var pending = (Task)typeof(MainWindow).GetProperty("HistoryDetailsPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            await pending.WaitAsync(TimeSpan.FromMinutes(2));
            double selectionReady = watch.Elapsed.TotalMilliseconds;
            maxGap = Math.Max(maxGap, uiClock.Elapsed.TotalMilliseconds - lastTick); heartbeat.Stop();
            if (!((TextBox)window.FindName("Details")).Text.Contains(((MainWindow.HistoryRow)history.SelectedItem).Id.ToString()))
                throw new InvalidDataException("Selected history did not finish loading.");
            window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            double copy, exact; long bytes; int matches;
            using (var journal = new Journal(journalPath, key))
            {
                watch.Restart(); new CopyService(journal, new Sink()).GenerateCodedAndCopy(fixtures[0].Stamp, s => StampRenderer.Png(StampRenderer.Render(s)));
                copy = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); var match = new VerificationService(journal).Image(fixtures[0].Png); exact = watch.Elapsed.TotalMilliseconds;
                matches = match.Matches.Count; if (match.Status != VerificationStatus.Match) throw new InvalidDataException("Exact match failed.");
                bytes = Directory.GetFiles(journalPath, "*.json").Sum(p => new FileInfo(p).Length);
            }
            CryptographicOperations.ZeroMemory(key);
            using var process = Process.GetCurrentProcess(); process.Refresh();
            results.Add(new { Generations = count, JournalBytes = bytes, StartupToRenderMs = startup,
                ConstructorBlockedUiMs = constructor, HistorySelectionBlockedUiMs = selection,
                HistoryDetailsReadyMs = selectionReady, HistoryReadUiTicks = uiTicks, HistoryReadMaxUiGapMs = maxGap,
                RenderAndThreeAppendsFakeClipboardMs = copy, ExactImageMs = exact, ExactMatches = matches,
                WorkingSetBytes = process.WorkingSet64, CumulativePeakWorkingSetBytes = process.PeakWorkingSet64 });
            Save(); Console.WriteLine($"{count}: startup={startup:F0}ms, select-handler={selection:F0}ms, details={selectionReady:F0}ms, UI max gap={maxGap:F0}ms, copy pipeline={copy:F0}ms");
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        var templates = fixtures.Select(f => new StampTemplate(f.Stamp, Normalize(f.Png))).ToArray();
        var timer = Stopwatch.StartNew(); var ranked = ImageSearch.Rank(templates[0].Ink, templates); timer.Stop();
        results.Add(new { Case = "Rank300Images", Milliseconds = timer.Elapsed.TotalMilliseconds, Candidates = ranked.Count }); Save();
        var dense = Enumerable.Repeat(true, 12_000_000).ToArray(); timer.Restart(); var found = ImageSearch.Detect(dense, 4000, 3000); timer.Stop();
        results.Add(new { Case = "DenseRed12Megapixels", Milliseconds = timer.Elapsed.TotalMilliseconds, Regions = found.Count }); Save();
    }
    static bool[] Normalize(byte[] png)
    {
        using var input = new MemoryStream(png); var bitmap = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var ink = ImageSearch.RedMask(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        return ImageSearch.Normalize(ink, bitmap.PixelWidth, bitmap.PixelHeight, ImageSearch.Bounds(ink, bitmap.PixelWidth, bitmap.PixelHeight)!);
    }
    sealed class Sink : IClipboard { public void Copy(byte[] png) { } }
}
