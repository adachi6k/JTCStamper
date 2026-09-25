using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using JTCStamper.App;
using JTCStamper.Core;

internal static class Program
{
    static string step = "validate-root";
    const string Marker = "JTCStamper synthetic migration test v1";
    // This derives a PUBLIC synthetic fixture password, never a real user's password.
    static string TestPassword => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(
        "JTC migration synthetic fixture only/" + (Environment.GetEnvironmentVariable("JTC_MIGRATION_TEST_CONTEXT") ?? throw new InvalidDataException("Missing test context.")))));
    static string IdentityHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)));
    static string MachineHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName)));
    sealed record Manifest(string SourceIdentity, string SourceMachine, string RecordsHash, string OriginalHash, string[] Images, int Records, int Generations, int Notes);
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static void Require(bool ok) { if (!ok) throw new InvalidDataException("Migration invariant failed."); }

    [STAThread]
    static int Main(string[] args)
    {
        string? root = null;
        bool validated = false;
        try
        {
            Require(args.Length == 2 && args[0] is "export" or "import");
            root = Path.GetFullPath(args[1]);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Require(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && File.ReadAllText(Path.Combine(root, ".migration-root")).Trim() == Marker);
            for (var d = new DirectoryInfo(root); d is not null; d = d.Parent) Require((d.Attributes & FileAttributes.ReparsePoint) == 0);
            validated = true;
            object detail = args[0] == "export" ? Export(root) : Import(root);
            File.WriteAllText(Path.Combine(root, "migration-" + args[0] + ".json"), JsonSerializer.Serialize(new
            {
                Status = "passed", Mode = args[0], OS = Environment.OSVersion.ToString(),
                SourceVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Detail = detail
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Migration " + args[0] + ": passed");
            return 0;
        }
        catch (Exception ex)
        {
            // No passphrases, key bytes, record payloads or private paths in diagnostics.
            if (validated)
            {
                try { File.WriteAllText(Path.Combine(root!, "migration-" + args[0] + ".json"), JsonSerializer.Serialize(new { Status = "failed", Mode = args[0], ErrorType = ex.GetType().Name, Step = step, HResult = $"0x{ex.HResult:X8}" })); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            Console.Error.WriteLine("Migration test failed at " + step + ": " + ex.GetType().Name);
            return 1;
        }
    }

    static object Export(string root)
    {
        var source = Path.Combine(root, "source"); Require(!Directory.Exists(source));
        var transfer = Path.Combine(root, "transfer"); Directory.CreateDirectory(transfer);
        step = "source-dpapi-key";
        var key = KeyStore.Load(source);
        try
        {
            step = "source-fixture";
            using var journal = new Journal(Path.Combine(source, "journal"), key);
            for (int i = 0; i < 3; i++)
            {
                var stamp = new Stamp("CI TEST", new DateOnly(2100, 1, i + 1), "TRANSFER",
                    i == 0 ? RingCode.PlainRenderer : RingCode.Renderer);
                var id = new CopyService(journal, new Sink()).GenerateCodedAndCopy(stamp, s => StampRenderer.Png(StampRenderer.Render(s)));
                journal.Annotate(id, "Synthetic migration note " + i);
            }
            var snapshot = new VerificationService(journal).ReadHistory();
            var records = snapshot.Entries.Select(x => x.Signed).ToArray();
            var original = JsonSerializer.SerializeToUtf8Bytes(records.First());
            var archive = Path.Combine(transfer, "fixture.jtcportable");
            step = "save-portable";
            HistoryBackup.SavePortable(archive, source, journal, TestPassword);
            var bytes = File.ReadAllBytes(archive);
            // Real Windows replacement rejection must preserve the previous archive.
            step = "locked-archive-preservation";
            using (var held = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { HistoryBackup.SavePortable(archive, source, journal, TestPassword); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                Require(failed && bytes.SequenceEqual(File.ReadAllBytes(archive)));
            }
            step = "export-manifest";
            var manifest = new Manifest(IdentityHash, MachineHash, Hash(JsonSerializer.SerializeToUtf8Bytes(records)),
                Hash(original), snapshot.Generations.Select(x => x.PngSha256).ToArray(), records.Length,
                snapshot.Generations.Count, snapshot.Entries.Count(x => x.Entry.Kind == "AnnotationAdded"));
            File.WriteAllText(Path.Combine(transfer, "manifest.json"), JsonSerializer.Serialize(manifest));
            return new { manifest.Records, manifest.Generations, manifest.Notes, ExistingArchivePreservedOnWriteFailure = true, SyntheticOnly = true };
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    static object Import(string root)
    {
        step = "different-identity";
        var transfer = Path.Combine(root, "transfer");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(transfer, "manifest.json")))!;
        Require(manifest.SourceIdentity != IdentityHash && manifest.SourceMachine != MachineHash);
        var archive = Path.Combine(transfer, "fixture.jtcportable");
        var destination = Path.Combine(root, "restored");
        step = "wrong-password";
        bool rejected = false;
        try { HistoryBackup.RestorePortable(archive, destination, "Incorrect synthetic password"); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected && !Directory.Exists(destination));
        step = "corrupted-archive";
        var corrupt = File.ReadAllBytes(archive); corrupt[corrupt.Length / 2] ^= 1;
        var badPath = Path.Combine(root, "corrupt.jtcportable"); File.WriteAllBytes(badPath, corrupt);
        rejected = false;
        try { HistoryBackup.RestorePortable(badPath, destination, TestPassword); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected && !Directory.Exists(destination));
        step = "restore-target-dpapi";
        HistoryBackup.RestorePortable(archive, destination, TestPassword);
        var key = KeyStore.Load(destination); // Actual target-user DPAPI unprotect after migration.
        try
        {
            step = "authenticate-restored-history";
            using var journal = new Journal(Path.Combine(destination, "journal"), key);
            var snapshot = new VerificationService(journal).ReadHistory();
            var records = snapshot.Entries.Select(x => x.Signed).ToArray();
            var original = JsonSerializer.SerializeToUtf8Bytes(records.First());
            Require(records.Length == manifest.Records && snapshot.Generations.Count == manifest.Generations &&
                snapshot.Entries.Count(x => x.Entry.Kind == "AnnotationAdded") == manifest.Notes &&
                Hash(JsonSerializer.SerializeToUtf8Bytes(records)) == manifest.RecordsHash && Hash(original) == manifest.OriginalHash &&
                snapshot.Generations.Select(x => x.PngSha256).SequenceEqual(manifest.Images) &&
                new VerificationService(journal).Original(Encoding.UTF8.GetString(original)).Status == VerificationStatus.Match);
            rejected = false;
            try { HistoryBackup.RestorePortable(archive, destination, TestPassword); }
            catch (IOException) { rejected = true; }
            Require(rejected && journal.Read().Select(x => x.Signed).SequenceEqual(records));
            return new { DifferentWindowsIdentity = true, DifferentMachine = true, TargetDpapiUnprotect = true,
                manifest.Records, manifest.Generations, manifest.Notes, ImagesAndOriginalPreserved = true,
                WrongPasswordRejected = true, CorruptionRejected = true, ExistingDestinationPreserved = true };
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    sealed class Sink : IClipboard { public void Copy(byte[] png) { Require(png.Length > 0); } }
}
