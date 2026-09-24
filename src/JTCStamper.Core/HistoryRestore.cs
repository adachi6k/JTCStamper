using System.Text.Json;

namespace JTCStamper.Core;

public static class HistoryRestore
{
    // Stage in the destination's parent, validate every record and stored image, then publish.
    // Never merge with or replace any existing destination, even an empty directory.
    public static void ToNewDirectory(string destination, byte[] key, IReadOnlyList<SignedEntry> records,
        Action<string> saveProtectedKey)
    {
        var target = Path.GetFullPath(destination);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("復元先は未使用の新しいフォルダーにしてください。");
        var parent = Path.GetDirectoryName(target) ?? throw new IOException("復元先が不正です。");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var staging = Path.Combine(parent, ".jtc-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var folder = Path.Combine(staging, "journal");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < records.Count; i++)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(records[i]);
                AtomicFile.Write(Path.Combine(folder, $"{i + 1:D12}.json"), stream => stream.Write(bytes), false);
            }
            using (var restored = new Journal(folder, key))
                _ = new VerificationService(restored).ReadGenerations();
            saveProtectedKey(Path.Combine(staging, "key.dpapi"));
            if (!File.Exists(Path.Combine(staging, "key.dpapi"))) throw new IOException("復元した鍵を保存できませんでした。");
            Directory.Move(staging, target);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
