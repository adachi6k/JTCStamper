using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.App;
using JTCStamper.Core;

internal static partial class Program
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
    static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    static void Case(string name, Func<object> test)
    {
        try { Result(name, "passed", test()); }
        catch (Exception ex) { Result(name, "failed", new { Type = ex.GetType().Name, ex.Message }); }
    }
    static string NewData(string folder)
    {
        var data = Path.Combine(folder, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, ".jtc-smoke-root"), "JTCStamper isolated smoke data v1"); return data;
    }
    static void Smoke(string exe, string data, string phase, bool runtime)
    {
        string report = Path.Combine(data, "report-" + phase + ".json"); File.Delete(report);
        var run = Start(exe, ["--smoke-test", "--test-root", data, "--phase", phase], "environment-" + phase, runtime);
        Require(run.ExitCode == 0 && File.Exists(report) && JsonDocument.Parse(File.ReadAllText(report)).RootElement.GetProperty("Passed").GetBoolean(),
            "Environment smoke failed, exit=" + run.ExitCode);
    }
    static void EnvironmentCases(string standard, string lite, string temp)
    {
        Case("standard-self-extraction-unwritable-fails-before-journal", () =>
        {
            var folder = Path.Combine(temp, "extraction-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var data = NewData(folder); var blocked = Path.Combine(folder, "not-a-directory"); File.WriteAllText(blocked, "preserve");
            var before = File.ReadAllBytes(blocked);
            var run = Start(standard, ["--smoke-test", "--test-root", data, "--phase", "seed"], "blocked-extraction", false, blocked);
            Require(run.ExitCode != 0 && !string.IsNullOrWhiteSpace(run.Error), "Missing host extraction failure diagnostic.");
            Require(File.ReadAllBytes(blocked).SequenceEqual(before) && !File.Exists(Path.Combine(data, "key.dpapi")) &&
                !Directory.Exists(Path.Combine(data, "journal")), "Extraction failure touched journal or blocker.");
            Smoke(standard, data, "seed", false);
            return new { run.ExitCode, DiagnosticPresent = true, ExistingFilePreserved = true, JournalNotStarted = true, Recovery = true,
                Scope = "Unwritable bundle extraction path, not an actual full TEMP volume." };
        });
        foreach (var (name, source, runtime) in new[] { ("Standard", standard, false), ("Lite", lite, true) })
        {
            Case(name + "-long-data-path-with-short-executable", () =>
            {
                string folder = Path.Combine(temp, "long-" + name + "-" + Guid.NewGuid().ToString("N"));
                for (int i = 0; i < 5; i++) folder = Path.Combine(folder, "日本語 空白_" + new string('x', 40));
                var data = NewData(folder);
                Require(data.Length > 300, "Long data path fixture too short.");
                Smoke(source, data, "seed", runtime); Smoke(source, data, "verify", runtime);
                return new { ExecutablePathCharacters = source.Length, JournalPathCharacters = Path.Combine(data, "journal").Length,
                    Scope = "Long data paths only. Executable paths over 260 characters failed in the separate probe and are unsupported.",
                    LongPathsEnabled = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", null),
                    Clipboard = "not requested" };
            });
            var previous = Path.Combine(root, "Previous" + name, "JTCStamper.App.exe");
            if (!File.Exists(previous)) { Result(name + "-previous-version-and-rollback", "skipped", "Previous package was not supplied."); continue; }
            Case(name + "-previous-version-and-rollback", () =>
            {
                var folder = Path.Combine(temp, "rollback-" + name + "-" + Guid.NewGuid().ToString("N"));
                var data = NewData(folder); var exe = Path.Combine(folder, "JTCStamper.App.exe");
                File.Copy(previous, exe); Smoke(exe, data, "seed", runtime);
                File.Copy(source, exe, true); Smoke(exe, data, "verify", runtime);
                byte[] before = File.ReadAllBytes(exe); bool rejected = false;
                using (var locked = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { File.Copy(previous, exe, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
                }
                Require(rejected && File.ReadAllBytes(exe).SequenceEqual(before), "Locked replacement changed EXE.");
                File.Copy(previous, exe, true); Smoke(exe, data, "verify", runtime);
                return new { PreviousSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(previous))),
                    CurrentSha256 = Convert.ToHexString(SHA256.HashData(before)), LockedUpdateRejected = rejected,
                    RollbackVerified = true, Scope = "Same-schema preview upgrade and rollback; no schema migration or new-version-only data." };
            });
        }
    }
    static int HoldClipboard(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(folder, ".owned-clipboard-lock"))) return 2;
        if (!OpenClipboard(IntPtr.Zero)) return 3;
        try
        {
            File.WriteAllText(Path.Combine(folder, "ready"), "locked");
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15) && !File.Exists(Path.Combine(folder, "release"))) Thread.Sleep(20);
            return 0;
        }
        finally { CloseClipboard(); }
    }
    static void ClipboardContention() => Case("real-clipboard-contention-records-failure-and-recovers", () =>
    {
        var folder = Path.Combine(root, "temp", "clipboard-lock-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".owned-clipboard-lock"), "Owned bounded clipboard contention test");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.ArgumentList.Add("--hold-clipboard"); info.ArgumentList.Add(folder);
        using var child = Process.Start(info) ?? throw new IOException("Lock child did not start.");
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "ready")) && !child.HasExited && watch.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);
            Require(File.Exists(Path.Combine(folder, "ready")), "Lock child could not obtain clipboard.");
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 30, 30, 190, 255, 30, 30, 190, 255, 30, 30, 190, 255, 30, 30, 190, 255 }, 8);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); byte[] png = stream.ToArray();
            using var journal = new Journal(Path.Combine(folder, "journal"), key);
            var copy = new CopyService(journal, new WindowsClipboard()); var stamp = new Stamp("TEST", new DateOnly(2100, 1, 1), "CHECK");
            Exception? failure = null;
            try { copy.GenerateCodedAndCopy(stamp, _ => png); } catch (Exception ex) { failure = ex; }
            Require(failure is ExternalException, "Actual clipboard failure was not observed.");
            Require(journal.Read().Select(r => r.Entry.Kind).SequenceEqual(new[] { "Generated", "CopyRequested", "CopyFailed" }),
                "Failed clipboard copy was marked completed or lost its failure record.");
            File.WriteAllText(Path.Combine(folder, "release"), "release"); Require(child.WaitForExit(5000), "Clipboard lock child did not exit.");
            copy.GenerateCodedAndCopy(stamp, _ => png); ownedClipboardHashes.Add(Convert.ToHexString(SHA256.HashData(png)));
            Require(journal.Read().Count == 6 && journal.Read().Last().Entry.Kind == "CopyCompleted", "Copy did not recover.");
            return new { ErrorType = failure!.GetType().Name, HResult = $"0x{failure.HResult:X8}", FailureRecorded = true, Recovered = true };
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "release"), "release");
            if (!child.HasExited && !child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
            CryptographicOperations.ZeroMemory(key);
        }
    });
    static void RememberOwnedClipboard(string data)
    {
        var path = Path.Combine(data, "key.dpapi"); if (!File.Exists(path)) return;
        var key = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            using var journal = new Journal(Path.Combine(data, "journal"), key);
            foreach (var generation in new VerificationService(journal).ReadGenerations()) ownedClipboardHashes.Add(generation.PngSha256);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
