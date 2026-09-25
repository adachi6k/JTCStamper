using System.Diagnostics;
using System.IO;

// Only a newly created, token-labelled <=256 MiB test volume is accepted. Never fill the host volume.
internal sealed class FullVolume : IDisposable
{
    readonly string root, label, filler;
    readonly List<string> metadataFiles = [];
    public int? LastError { get; private set; }
    public long? FreeBytesAtFailure { get; private set; }
    public long TotalBytes => new DriveInfo(root).TotalSize;
    public FullVolume(string root, string token)
    {
        this.root = Path.GetFullPath(root);
        label = "JTCFULL_" + token[..8];
        Validate();
        Require(File.ReadAllText(Path.Combine(root, ".jtc-full-volume")).Trim() == token, "Volume marker mismatch.");
        filler = Path.Combine(root, "filler-" + token);
        Require(!Directory.Exists(filler), "Filler directory already exists.");
        Directory.CreateDirectory(filler);
    }
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    void Validate()
    {
        Require(root == Path.GetPathRoot(root) && !root.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase), "Only a separate drive root is allowed.");
        var drive = new DriveInfo(root);
        Require(drive.IsReady && drive.DriveType == DriveType.Fixed && drive.DriveFormat == "NTFS" &&
            drive.TotalSize is >= 32 * 1024 * 1024 and <= 256 * 1024 * 1024 && drive.VolumeLabel == label,
            "Refusing to fill a non-test volume.");
    }
    public static bool IsDiskFull(IOException ex) => (ex.HResult & 0xffff) is 39 or 112;
    public void Fill(string directory)
    {
        Validate(); Release();
        directory = Path.GetFullPath(directory);
        Require(directory.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Target must be on the test volume.");
        for (var d = new DirectoryInfo(directory); d is not null; d = d.Parent)
            Require((d.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse targets are not allowed."); LastError = null; FreeBytesAtFailure = null;
        var timer = Stopwatch.StartNew();
        long written = 0;
        var buffer = new byte[1024 * 1024];
        // Actual writes allocate space; SetLength alone can leave unallocated ranges.
        using (var stream = new FileStream(Path.Combine(filler, "data.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
        {
            foreach (int size in new[] { buffer.Length, 4096, 512 })
            {
                while (true)
                {
                    Require(timer.Elapsed < TimeSpan.FromSeconds(90) && written <= 256L * 1024 * 1024, "Filler limit exceeded.");
                    try { stream.Write(buffer, 0, size); stream.Flush(true); written += size; }
                    catch (IOException ex) when (IsDiskFull(ex)) { LastError = ex.HResult & 0xffff; break; }
                }
            }
        }
        // Match the real temporary-file names and directory. A failure in another directory's
        // NTFS index is insufficient: that directory may be full while the target still has room.
        int consecutiveFailures = 0;
        for (int i = 0; i < 100000; i++)
        {
            Require(timer.Elapsed < TimeSpan.FromSeconds(120), "Metadata filler timed out.");
            string path = Path.Combine(directory, ".jtc-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                metadataFiles.Add(path); consecutiveFailures = 0;
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                LastError = ex.HResult & 0xffff;
                if (++consecutiveFailures < 128) continue;
                FreeBytesAtFailure = new DriveInfo(root).AvailableFreeSpace;
                return;
            }
        }
        throw new IOException("Could not exhaust metadata within the bounded file count.");
    }
    public void Release()
    {
        Validate();
        if (!Directory.Exists(filler)) return;
        foreach (var file in Directory.GetFiles(filler)) File.Delete(file);
        foreach (var file in metadataFiles) File.Delete(file);
        metadataFiles.Clear();
    }
    public void Dispose() { Release(); Directory.Delete(filler); }
}
