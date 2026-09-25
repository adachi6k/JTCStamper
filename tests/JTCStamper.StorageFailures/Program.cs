using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JTCStamper.App;
using JTCStamper.Core;

internal static class Program
{
    const string Marker = "JTCStamper isolated storage interruption data v1";
    static readonly Stamp Stamp = new("TEST", new DateOnly(2100, 1, 1), "CHECK");
    sealed record Result(string Name, string Status, int? RetainedRecords, string? Error);
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    static byte[] Render(Stamp stamp) => StampRenderer.Png(StampRenderer.Render(stamp));

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Require(args.Length is 2 or 3 && args[0] is "--run" or "--child", "Invalid arguments.");
            var root = Path.GetFullPath(args[1]);
            ValidateRoot(root);
            if (args[0] == "--child")
            {
                Require(args.Length == 3, "Missing checkpoint.");
                Child(root, args[2]); return 2; // A checkpoint child must be terminated by its parent.
            }
            Require(args.Length == 2, "Unexpected arguments.");
            var results = new List<Result>();
            foreach (string checkpoint in new[] { "partial-key", "before-generation", "after-generation", "during-copy", "after-copy" })
            {
                try { results.Add(new(checkpoint, "passed", RunCase(root, checkpoint), null)); }
                catch (Exception ex) { results.Add(new(checkpoint, "failed", null, ex.GetType().Name + ": " + ex.Message)); }
            }
            File.WriteAllText(Path.Combine(root, "storage-failures.json"), JsonSerializer.Serialize(new
            {
                Schema = 1, CreatedUtc = DateTimeOffset.UtcNow, OS = Environment.OSVersion.ToString(),
                RunnerSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))),
                Scope = "Owned child process termination; synthetic data and clipboard sink; not physical disk exhaustion or power loss. Partial key checkpoint exercises the shared AtomicFile publication helper with DPAPI bytes.",
                Results = results
            }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var result in results) Console.WriteLine($"{result.Status}: {result.Name} {result.Error}");
            return results.All(x => x.Status == "passed") ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
    }

    static void ValidateRoot(string root)
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        Require(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase), "Root must be inside TEMP.");
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            Require((dir.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse points are not allowed.");
        var marker = Path.Combine(root, ".storage-failure-root");
        Require((File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0 && File.ReadAllText(marker).Trim() == Marker, "Missing test marker.");
    }

    static int RunCase(string root, string checkpoint)
    {
        var folder = Path.Combine(root, checkpoint + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".storage-failure-root"), Marker);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        // dotnet run executes a native apphost. Single-file publication also launches itself.
        Require(!Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase), "Use the native apphost.");
        start.ArgumentList.Add("--child"); start.ArgumentList.Add(folder); start.ArgumentList.Add(checkpoint);
        using (var child = Process.Start(start) ?? throw new IOException("Child did not start."))
        {
            try
            {
                var watch = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(folder, "ready")))
                {
                    Require(!child.WaitForExit(50), "Child exited before checkpoint.");
                    Require(watch.Elapsed < TimeSpan.FromSeconds(30), "Checkpoint timed out.");
                }
                child.Kill();
                Require(child.WaitForExit(10000), "Owned child did not terminate.");
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(10000); } }
        }
        var keyPath = Path.Combine(folder, "key.dpapi");
        byte[]? previousKey = File.Exists(keyPath) ? File.ReadAllBytes(keyPath) : null;
        if (checkpoint == "partial-key")
            Require(previousKey is null && Directory.GetFiles(folder, ".jtc-write-*.tmp").Any(x => new FileInfo(x).Length > 0), "Partial key became authoritative, or interruption did not leave a partial file.");
        else Require(previousKey is not null, "Committed key was lost.");
        var key = KeyStore.Load(folder);
        try
        {
            var second = KeyStore.Load(folder);
            try { Require(key.Length == 32 && key.SequenceEqual(second), "Restart did not load a stable key."); }
            finally { CryptographicOperations.ZeroMemory(second); }
            if (previousKey is not null) Require(previousKey.SequenceEqual(File.ReadAllBytes(keyPath)), "Restart replaced the existing key.");
            using var journal = new Journal(Path.Combine(folder, "journal"), key);
            var before = journal.Read();
            int expected = checkpoint switch { "after-generation" => 1, "during-copy" => 2, "after-copy" => 3, _ => 0 };
            Require(before.Count == expected && before.Count(x => x.Entry.Kind == "CopyCompleted") == (checkpoint == "after-copy" ? 1 : 0), "Interrupted copy was reported complete.");
            if (expected > 0) Require(new VerificationService(journal).ReadHistory().Generations.Count == 1, "Retained generation failed semantic verification.");
            var clipboard = new Sink();
            new CopyService(journal, clipboard).GenerateCodedAndCopy(Stamp, Render);
            var after = journal.Read();
            Require(clipboard.Calls == 1 && after.Count == expected + 3 && after[^1].Entry.Kind == "CopyCompleted" &&
                before.Select(x => x.Signed.Mac).SequenceEqual(after.Take(expected).Select(x => x.Signed.Mac)), "Restart failed or rewrote retained history.");
            return expected;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    static void Child(string root, string checkpoint)
    {
        Require(checkpoint is "partial-key" or "before-generation" or "after-generation" or "during-copy" or "after-copy", "Invalid checkpoint.");
        Require(!File.Exists(Path.Combine(root, "key.dpapi")) && !Directory.Exists(Path.Combine(root, "journal")), "Child root must be empty.");
        if (checkpoint == "partial-key")
        {
            var secret = RandomNumberGenerator.GetBytes(32);
            try
            {
                var encrypted = ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser);
                AtomicFile.Write(Path.Combine(root, "key.dpapi"), stream =>
                {
                    stream.Write(encrypted.AsSpan(0, encrypted.Length / 2));
                    ((FileStream)stream).Flush(true); Checkpoint(root);
                }, overwrite: false);
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
            return;
        }
        var key = KeyStore.Load(root);
        try
        {
            using var journal = new Journal(Path.Combine(root, "journal"), key);
            if (checkpoint == "before-generation") Checkpoint(root);
            if (checkpoint == "after-generation")
            {
                var id = Guid.NewGuid(); var stamp = Stamp with { GeometryCode = RingCode.ForEvent(id) };
                var png = Render(stamp);
                journal.Append("Generated", id, new Generation(id, DateTimeOffset.UtcNow, stamp, Convert.ToHexString(SHA256.HashData(png)), Convert.ToBase64String(png)));
                Checkpoint(root);
            }
            new CopyService(journal, new Sink(checkpoint == "during-copy" ? () => Checkpoint(root) : null)).GenerateCodedAndCopy(Stamp, Render);
            Checkpoint(root);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    static void Checkpoint(string root)
    {
        File.WriteAllText(Path.Combine(root, "ready"), "ready");
        // Do not leave an orphan indefinitely if the parent is interrupted.
        Thread.Sleep(TimeSpan.FromSeconds(60));
        throw new TimeoutException("Parent did not terminate checkpoint child.");
    }
    sealed class Sink(Action? copied = null) : IClipboard
    {
        public int Calls { get; private set; }
        public void Copy(byte[] png) { Require(png.Length > 0, "Empty PNG."); Calls++; copied?.Invoke(); }
    }
}
