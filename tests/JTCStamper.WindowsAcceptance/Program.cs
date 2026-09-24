using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.Core;

internal static class Program
{
    static readonly List<object> Results = [];
    static string root = "";
    static bool failed;
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length is not (1 or 2) || (args.Length == 2 && args[1] != "--skip-office")) return 2;
        bool skipOffice = args.Length == 2;
        root = Path.GetFullPath(args[0]);
        if (!File.Exists(Path.Combine(root, ".acceptance-root"))) return 2;
        foreach (var edition in new[] { "standard", "lite" })
            foreach (var phase in new[] { "seed", "restart", "replacement" })
                File.Delete(Path.Combine(root, edition + "-" + phase + ".json"));
        File.Delete(Path.Combine(root, "acceptance.json"));
        var temp = Path.Combine(root, "temp"); Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
        var standard = Path.Combine(root, "Standard", "JTCStamper.App.exe");
        var lite = Path.Combine(root, "Lite", "JTCStamper.App.exe");
        DataObject? saved = null; bool clipboardReady = false;
        try
        {
            try { saved = CloneClipboard(); clipboardReady = true; }
            catch (Exception ex) { Result("clipboard-preservation", "skipped", ex.GetType().Name); }
            var absent = Start(lite, ["--smoke-test"], "missing-runtime", false);
            Result("lite-runtime-missing-diagnostic", absent.ExitCode != 0 && absent.Error.Contains(".NET") ? "passed" : "failed", new { absent.ExitCode });
            foreach (var (name, initial, replacement) in new[] { ("standard", standard, lite), ("lite", lite, standard) })
            {
                var folder = Path.Combine(temp, "日本語 空白-" + name + "-" + Guid.NewGuid().ToString("N"));
                var data = Path.Combine(folder, "data"); Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(data, ".jtc-smoke-root"), "JTCStamper isolated smoke data v1");
                var exe = Path.Combine(folder, "JTCStamper.App.exe"); File.Copy(initial, exe);
                bool groupPassed = true;
                foreach (var phase in new[] { "seed", "restart", "replacement" })
                {
                    if (phase == "replacement") File.Copy(replacement, exe, true);
                    var testPhase = phase == "seed" ? "seed" : "verify";
                    var arguments = new List<string> { "--smoke-test", "--test-root", data, "--phase", testPhase };
                    if (clipboardReady) arguments.Add("--clipboard");
                    // Standard seed/restart also prove operation with no discoverable .NET 10 runtime.
                    bool runtime = name == "standard" ? phase == "replacement" : phase != "replacement";
                    var run = Start(exe, arguments, name + "-" + phase, runtime);
                    var report = Path.Combine(data, "report-" + testPhase + ".json");
                    var passed = run.ExitCode == 0 && File.Exists(report) && JsonDocument.Parse(File.ReadAllText(report)).RootElement.GetProperty("Passed").GetBoolean();
                    Result(name + "-" + phase, passed ? "passed" : "failed", new { run.ExitCode, Clipboard = clipboardReady, PrivateRuntime = runtime });
                    if (File.Exists(report)) File.Copy(report, Path.Combine(root, name + "-" + phase + ".json"), true);
                    if (!passed) { groupPassed = false; break; }
                }
                if (clipboardReady && !skipOffice && groupPassed)
                    RunOfficeSuite(data, name + "-");
            }
            if (skipOffice) Result("office-paste-save-reopen", "skipped", "Explicit diagnostic option --skip-office.");
        }
        catch (Exception ex) { Result("runner", "failed", ex.ToString()); }
        finally
        {
            if (clipboardReady)
            {
                try { if (saved is null) Clipboard.Clear(); else Clipboard.SetDataObject(saved, true); Result("original-clipboard-restored", "passed"); }
                catch (Exception ex) { Result("original-clipboard-restored", "failed", ex.Message); }
            }
            File.WriteAllText(Path.Combine(root, "acceptance.json"), JsonSerializer.Serialize(new
            {
                Os = Environment.OSVersion.VersionString, Utc = DateTimeOffset.UtcNow,
                IsElevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator),
                OfficeBuild = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\ClickToRun\Configuration", "VersionToReport", null),
                StandardSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(standard))),
                LiteSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lite))), Results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed ? 1 : 0;
    }
    static void Result(string name, string status, object? detail = null)
    {
        failed |= status == "failed";
        Results.Add(new { Name = name, Status = status, Detail = detail }); Console.WriteLine(name + ": " + status);
    }
    static (int ExitCode, string Error) Start(string exe, IEnumerable<string> arguments, string label, bool runtime)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["DOTNET_ROOT_X64"] = Path.Combine(root, runtime ? "runtime" : "missing-runtime");
        info.Environment["DOTNET_ROOT"] = info.Environment["DOTNET_ROOT_X64"];
        info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0"; info.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        using var process = Process.Start(info) ?? throw new IOException("Cannot start test process.");
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(90000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException(label); }
        Task.WaitAll(error, output);
        return (process.ExitCode, error.Result);
    }
    static DataObject? CloneClipboard()
    {
        uint sequence = JTCStamper.App.NativeClipboard.SequenceNumber;
        var original = Clipboard.GetDataObject(); if (original is null) return null;
        var clone = new DataObject();
        foreach (var format in original.GetFormats(false))
        {
            var value = format == "PNG"
                ? new MemoryStream(JTCStamper.App.NativeClipboard.ReadPng().Bytes ?? throw new InvalidDataException("PNG clipboard unavailable."))
                : original.GetData(format, false);
            value = value switch
            {
                MemoryStream stream when stream.Length <= 32 * 1024 * 1024 => new MemoryStream(stream.ToArray()),
                BitmapSource image => image.CloneCurrentValue(),
                string[] strings => strings.ToArray(), byte[] bytes => bytes.ToArray(),
                string or int or bool => value,
                _ => throw new InvalidDataException("Cannot safely preserve clipboard format.")
            };
            clone.SetData(format, value, false);
        }
        if (sequence != JTCStamper.App.NativeClipboard.SequenceNumber) throw new InvalidDataException("Clipboard changed during preservation.");
        return clone;
    }
    static void RunOfficeSuite(string dataRoot, string prefix)
    {
        var png = JTCStamper.App.NativeClipboard.ReadPng().Bytes
            ?? throw new InvalidDataException("PNG clipboard missing after smoke.");
        var key = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(dataRoot, "key.dpapi")), null, DataProtectionScope.CurrentUser);
        try
        {
            using var journal = new Journal(Path.Combine(dataRoot, "journal"), key);
            if (new VerificationService(journal).Image(png).Status != VerificationStatus.Match)
                throw new InvalidDataException("Office source has no matching generation.");
            Result(prefix + "office-source-matches-history", "passed");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        foreach (var product in new[] { "Word", "Excel", "PowerPoint" })
        {
            try { OfficePaste(product, png, prefix); }
            catch (Exception ex) { Result(prefix + product + "-paste-save-reopen", "failed", ex.GetBaseException().Message); }
        }
    }
    static void OfficePaste(string product, byte[] png, string prefix)
    {
        // Only create and close synthetic documents. Never quit a pre-existing Office process.
        var processName = product == "Word" ? "WINWORD" : product == "Excel" ? "EXCEL" : "POWERPNT";
        bool existed = Process.GetProcessesByName(processName).Length != 0;
        if (existed && product != "PowerPoint") { Result(prefix + product + "-paste-save-reopen", "skipped", "Existing user application; left untouched."); return; }
        var type = Type.GetTypeFromProgID(product + ".Application") ?? throw new NotSupportedException(product + " is not installed.");
        dynamic app = Activator.CreateInstance(type)!;
        dynamic? document = null; string version = app.Version;
        var extension = product == "Word" ? ".docx" : product == "Excel" ? ".xlsx" : ".pptx";
        var path = Path.Combine(root, product + "-synthetic-" + Guid.NewGuid().ToString("N") + extension);
        int count; double width, height;
        try
        {
            if (product == "Word")
            {
                document = app.Documents.Add(); document.Content.Paste();
                count = (int)document.InlineShapes.Count; width = (double)document.InlineShapes[1].Width; height = (double)document.InlineShapes[1].Height;
                document.SaveAs2(path, 16); document.Close(0); document = null; document = app.Documents.Open(path, ReadOnly: true);
                if ((int)document.InlineShapes.Count != count) throw new InvalidDataException("Word image lost after reopen.");
            }
            else if (product == "Excel")
            {
                document = app.Workbooks.Add(); dynamic sheet = document.Worksheets[1]; sheet.Paste();
                count = (int)sheet.Shapes.Count; width = (double)sheet.Shapes.Item(1).Width; height = (double)sheet.Shapes.Item(1).Height;
                document.SaveAs(path, 51); document.Close(false); document = null; document = app.Workbooks.Open(path, ReadOnly: true);
                if ((int)document.Worksheets[1].Shapes.Count != count) throw new InvalidDataException("Excel image lost after reopen.");
            }
            else
            {
                document = app.Presentations.Add(0); dynamic slide = document.Slides.Add(1, 12); slide.Shapes.PasteSpecial(6);
                count = (int)slide.Shapes.Count; width = (double)slide.Shapes.Item(1).Width; height = (double)slide.Shapes.Item(1).Height;
                document.SaveAs(path, 24); document.Close(); document = null; document = app.Presentations.Open(path, -1, 0, 0);
                if ((int)document.Slides[1].Shapes.Count != count) throw new InvalidDataException("PowerPoint image lost after reopen.");
            }
            if (count < 1 || width <= 0 || height <= 0) throw new InvalidDataException("Missing pasted picture.");
            double best = 0; bool pixelsPreserved = false, pngBytesPreserved = false;
            var original = Raster(png);
            int transparentPixels = Enumerable.Range(0, original.Width * original.Height).Count(i => original.Pixels[i * 4 + 3] == 0);
            var reference = Normalize(png);
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries.Where(e => e.FullName.Contains("/media/") && e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                using var bytes = new MemoryStream(); using (var source = entry.Open()) source.CopyTo(bytes);
                var extracted = bytes.ToArray();
                var raster = Raster(extracted);
                pixelsPreserved |= raster.Width == original.Width && raster.Height == original.Height && raster.Pixels.SequenceEqual(original.Pixels);
                pngBytesPreserved |= extracted.SequenceEqual(png);
                var image = Normalize(extracted);
                var stamp = new Stamp("TEST", new DateOnly(2100, 1, 1), "TEST", RingCode.PlainRenderer);
                var candidates = ImageSearch.Rank(image, [new(stamp, reference)]);
                if (candidates.Count > 0) best = Math.Max(best, candidates[0].Score);
            }
            bool dimensionsPreserved = Math.Abs(width - original.Width / original.DpiX * 72) < 0.1 && Math.Abs(height - original.Height / original.DpiY * 72) < 0.1;
            Result(prefix + product + "-paste-save-reopen", best >= 0.98 && pixelsPreserved && dimensionsPreserved && transparentPixels > 0 ? "passed" : "failed", new
            {
                Version = version, Pictures = count, WidthPoints = width, HeightPoints = height,
                ExtractedImageShapeScore = best, PixelsIncludingAlphaPreserved = pixelsPreserved,
                PngBytesPreserved = pngBytesPreserved, DimensionsPreserved = dimensionsPreserved,
                OriginalTransparentPixels = transparentPixels
            });
        }
        finally
        {
            try
            {
                if (document is not null) { if (product == "Word") document.Close(0); else if (product == "Excel") document.Close(false); else document.Close(); }
            }
            finally
            {
                if (!existed)
                {
                    int remaining = product == "Word" ? (int)app.Documents.Count : product == "Excel" ? (int)app.Workbooks.Count : (int)app.Presentations.Count;
                    if (remaining == 0) app.Quit(); // Leave any document opened by the user during testing alone.
                }
            }
        }
    }
    static (int Width, int Height, double DpiX, double DpiY, byte[] Pixels) Raster(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return (image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, pixels);
    }
    static bool[] Normalize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        var mask = ImageSearch.RedMask(pixels, image.PixelWidth, image.PixelHeight);
        var bounds = ImageSearch.Bounds(mask, image.PixelWidth, image.PixelHeight) ?? throw new InvalidDataException("No red stamp found.");
        return ImageSearch.Normalize(mask, image.PixelWidth, image.PixelHeight, bounds);
    }
}
