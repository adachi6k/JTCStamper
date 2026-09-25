using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using JTCStamper.App;
using JTCStamper.Core;
using static FullVolume;

internal static class Program
{
    static readonly Stamp Stamp = new("TEST", new DateOnly(2100, 1, 1), "CHECK");
    static readonly List<object> Results = [];
    static FullVolume volume = null!;
    static string root = "", reportRoot = "";
    static byte[] Render(Stamp stamp) => StampRenderer.Png(StampRenderer.Render(stamp));

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Require(args.Length == 3 && Guid.TryParseExact(args[2], "N", out _), "Expected volume root, report root and token.");
            root = Path.GetFullPath(args[0]); reportRoot = Path.GetFullPath(args[1]);
            Require(Path.GetPathRoot(root) != Path.GetPathRoot(reportRoot) && Directory.Exists(reportRoot), "Reports must use an existing folder outside the full volume.");
            using var full = new FullVolume(root, args[2]); volume = full;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("ApplicationXamlSource")!)
            {
                var source = XDocument.Load(stream); var ns = source.Root!.Name.Namespace;
                var dictionary = new XElement(ns + "ResourceDictionary", source.Root.Attributes().Where(x => x.IsNamespaceDeclaration),
                    source.Root.Element(ns + "Application.Resources")!.Elements());
                app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            }
            int exitCode = 0;
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try { await Run(); }
                catch (Exception ex) { exitCode = 1; Results.Add(new { Name = "fatal", Status = "failed", Error = ex.ToString() }); }
                finally
                {
                    File.WriteAllText(Path.Combine(reportRoot, "disk-full.json"), JsonSerializer.Serialize(new
                    {
                        SourceVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                        OS = Environment.OSVersion.ToString(), Utc = DateTimeOffset.UtcNow,
                        VolumeBytes = volume.TotalBytes, Scope = "Real NTFS exhaustion on a dedicated fixed VHD; synthetic clipboard; no host disk filling or power loss.",
                        Results
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    app.Shutdown(exitCode);
                }
            });
            app.Run(); return exitCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
    }

    static async Task Check(string name, Func<Task<object>> test)
    {
        try
        {
            var detail = await test();
            Results.Add(new { Name = name, Status = "passed", Detail = detail });
            Console.WriteLine("passed: " + name);
        }
        catch (Exception ex)
        {
            Results.Add(new { Name = name, Status = "failed", Error = ex.ToString() });
            Console.WriteLine("failed: " + name + " " + ex.Message);
        }
        finally { volume.Release(); }
    }
    static IOException ExpectFull(Action action)
    {
        try { action(); }
        catch (IOException ex) when (IsDiskFull(ex)) { return ex; }
        throw new InvalidDataException("Expected an actual Windows disk-full error (39 or 112).");
    }
    static object Evidence(IOException error, int? records = null, int? clipboardCalls = null) =>
        new { Win32Error = error.HResult & 0xffff, HResult = $"0x{error.HResult:X8}", volume.FreeBytesAtFailure, RetainedRecords = records, ClipboardCalls = clipboardCalls, Recovered = true };
    static string CaseFolder()
    {
        var path = Path.Combine(root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    static async Task Run()
    {
        await Check("initial-key-full-no-partial-key-and-recovery", () =>
        {
            var folder = CaseFolder();
            volume.Fill(folder);
            var error = ExpectFull(() => KeyStore.Load(folder));
            Require(!File.Exists(Path.Combine(folder, "key.dpapi")), "A failed key was published.");
            volume.Release();
            var key = KeyStore.Load(folder);
            try
            {
                var again = KeyStore.Load(folder);
                try { Require(key.Length == 32 && key.SequenceEqual(again), "Key recovery failed."); }
                finally { CryptographicOperations.ZeroMemory(again); }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            return Task.FromResult(Evidence(error));
        });

        foreach (string boundary in new[] { "Generated", "CopyRequested", "CopyCompleted" })
            await Check("copy-full-at-" + boundary, () =>
            {
                var folder = CaseFolder(); var key = KeyStore.Load(folder);
                try
                {
                    using var journal = new Journal(Path.Combine(folder, "journal"), key);
                    var clipboard = new Sink();
                    var writer = new BoundaryWriter(journal, boundary, () => volume.Fill(Path.Combine(folder, "journal")));
                    var error = ExpectFull(() => new CopyService(writer, clipboard).GenerateCodedAndCopy(Stamp, Render));
                    int expected = boundary == "Generated" ? 0 : boundary == "CopyRequested" ? 1 : 2;
                    var before = journal.Read();
                    Require(before.Count == expected && !before.Any(x => x.Entry.Kind == "CopyCompleted") &&
                        clipboard.Calls == (expected == 2 ? 1 : 0), "Incorrect completion or clipboard order.");
                    if (expected > 0) Require(new VerificationService(journal).ReadHistory().Generations.Count == 1, "Retained generation failed verification.");
                    volume.Release();
                    new CopyService(journal, clipboard).GenerateCodedAndCopy(Stamp, Render);
                    var after = journal.Read();
                    Require(after.Count == expected + 3 && before.Select(x => x.Signed.Mac).SequenceEqual(after.Take(expected).Select(x => x.Signed.Mac)), "Recovery changed existing history.");
                    return Task.FromResult(Evidence(error, expected, expected == 2 ? 1 : 0));
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            });

        await Check("original-and-backup-full-preserve-existing-and-recover", () =>
        {
            var folder = CaseFolder(); var key = KeyStore.Load(folder);
            try
            {
                using var journal = new Journal(Path.Combine(folder, "journal"), key);
                new CopyService(journal, new Sink()).GenerateCodedAndCopy(Stamp, Render);
                var original = Path.Combine(folder, "original.jtc"); var backup = Path.Combine(folder, "backup.jtcbackup");
                void SaveOriginal() => AtomicFile.Write(original, s => s.Write(JsonSerializer.SerializeToUtf8Bytes(journal.Read().First().Signed)));
                SaveOriginal(); HistoryBackup.Save(backup, folder, journal);
                var oldOriginal = File.ReadAllBytes(original); var oldBackup = File.ReadAllBytes(backup);
                volume.Fill(folder);
                var originalError = ExpectFull(SaveOriginal);
                var backupError = ExpectFull(() => HistoryBackup.Save(backup, folder, journal));
                Require(oldOriginal.SequenceEqual(File.ReadAllBytes(original)) && oldBackup.SequenceEqual(File.ReadAllBytes(backup)), "Failed overwrite damaged existing files.");
                volume.Release();
                SaveOriginal(); HistoryBackup.Save(backup, folder, journal);
                var restored = Path.Combine(folder, "restored");
                HistoryBackup.Restore(backup, restored);
                using var reopened = new Journal(Path.Combine(restored, "journal"), key);
                Require(reopened.Read().Count == 3 && new VerificationService(reopened).ReadHistory().Generations.Count == 1, "Recovered backup could not be restored.");
                return Task.FromResult<object>(new { Original = Evidence(originalError), Backup = Evidence(backupError), ExistingFilesPreserved = true, RestoreVerified = true });
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        });

        foreach (bool afterClipboard in new[] { false, true })
            await Check(afterClipboard ? "window-full-after-copy" : "window-full-before-copy", async () =>
            {
                var folder = CaseFolder(); var window = new MainWindow(folder);
                int observedCalls;
                try
                {
                    await window.InitializationPending;
                    var clipboard = new Sink(afterClipboard ? () => volume.Fill(Path.Combine(folder, "journal")) : null);
                    if (!afterClipboard) volume.Fill(Path.Combine(folder, "journal"));
                    bool success = await window.CopyAsync(clipboard);
                    observedCalls = clipboard.Calls;
                    Require(!success && !window.IsStoreBusy && observedCalls == (afterClipboard ? 1 : 0) &&
                        ((TextBlock)window.FindName("Status")).Text.Contains("成功扱いにしていません"), "Window reported success or failed to show storage failure.");
                    volume.Release();
                    Require(await window.CopyAsync(new Sink()), "Window did not recover after releasing space.");
                }
                finally { volume.Release(); window.ClearNoteDrafts(); window.Close(); }
                var key = KeyStore.Load(folder);
                try
                {
                    using var journal = new Journal(Path.Combine(folder, "journal"), key);
                    Require(journal.Read().Count == (afterClipboard ? 5 : 3) && journal.Read().Count(x => x.Entry.Kind == "CopyCompleted") == 1, "Window recovery produced incorrect history.");
                }
                finally { CryptographicOperations.ZeroMemory(key); }
                return new { FillerWin32Error = volume.LastError, volume.FreeBytesAtFailure, ClipboardCallsAtFailure = observedCalls, SuccessReported = false, Recovered = true };
            });

        // Check failures are recorded individually; make the overall process fail too.
        Require(Results.All(x => (string?)x.GetType().GetProperty("Status")!.GetValue(x) == "passed"), "One or more disk-full cases failed.");
    }

    sealed class BoundaryWriter(Journal journal, string target, Action fill) : IJournalWriter
    {
        public SignedEntry Append(string kind, Guid eventId, object payload)
        {
            if (kind == target) fill(); // No injected exception: the actual filesystem write below must fail.
            return journal.Append(kind, eventId, payload);
        }
    }
    sealed class Sink(Action? copied = null) : IClipboard
    {
        public int Calls { get; private set; }
        public void Copy(byte[] png) { Require(png.Length > 0, "Empty image."); Calls++; copied?.Invoke(); }
    }
}
