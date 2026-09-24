using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JTCStamper.Core;

namespace JTCStamper.App;

// Version 1 is deliberately bound to the current Windows user. Not a cross-PC migration format.
internal static class HistoryBackup
{
    const int MaxBytes = 256 * 1024 * 1024;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("JTC-BACKUP-DPAPI-1\n");
    sealed record Payload(int Version, byte[] ProtectedKey, SignedEntry[] Records);

    internal static void Save(string path, string root, Journal journal)
    {
        if (!Path.GetExtension(path).Equals(".jtcbackup", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("バックアップの拡張子は .jtcbackup にしてください。");
        var protectedKey = File.ReadAllBytes(Path.Combine(root, "key.dpapi"));
        var key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
        byte[]? plain = null;
        try
        {
            if (key.Length != 32) throw new InvalidDataException("秘密鍵の長さが不正です。");
            var records = journal.Read().Select(x => x.Signed).ToArray();
            // The key file could have been replaced since this journal was opened.
            foreach (var record in records)
                if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(record.EntryJson)), Convert.FromHexString(record.Mac)))
                    throw new InvalidDataException("保存先の鍵と開いている履歴が一致しません。");
            _ = new VerificationService(journal).ReadGenerations();
            plain = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, protectedKey, records));
            if (plain.Length > MaxBytes) throw new InvalidDataException("バックアップは256 MiB以内の履歴に対応しています。");
            var encrypted = ProtectedData.Protect(plain, Magic, DataProtectionScope.CurrentUser);
            if (encrypted.Length > MaxBytes) throw new InvalidDataException("バックアップのサイズ上限を超えました。");
            AtomicFile.Write(path, output => { output.Write(Magic); output.Write(encrypted); });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    internal static void Restore(string path, string destination)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= Magic.Length || stream.Length > MaxBytes + Magic.Length)
            throw new InvalidDataException("バックアップのサイズが不正です（上限256 MiB）。");
        var header = new byte[Magic.Length]; stream.ReadExactly(header);
        if (!header.SequenceEqual(Magic)) throw new InvalidDataException("対応していないバックアップ形式です。");
        var encrypted = new byte[checked((int)(stream.Length - Magic.Length))]; stream.ReadExactly(encrypted);
        byte[] plain;
        try { plain = ProtectedData.Unprotect(encrypted, Magic, DataProtectionScope.CurrentUser); }
        catch (CryptographicException ex) { throw new InvalidDataException("バックアップを復号できません。同じWindowsユーザー環境か、ファイルが破損していないか確認してください。", ex); }
        byte[]? key = null;
        try
        {
            if (plain.Length > MaxBytes) throw new InvalidDataException("バックアップのサイズ上限を超えました。");
            var data = JsonSerializer.Deserialize<Payload>(plain) ?? throw new InvalidDataException("バックアップが空です。");
            if (data.Version != 1 || data.ProtectedKey is null || data.Records is null || data.Records.Any(x => x is null))
                throw new InvalidDataException("バックアップの内容が不正です。");
            key = ProtectedData.Unprotect(data.ProtectedKey, null, DataProtectionScope.CurrentUser);
            HistoryRestore.ToNewDirectory(destination, key, data.Records,
                keyPath => AtomicFile.Write(keyPath, output => output.Write(data.ProtectedKey), false));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }
}
