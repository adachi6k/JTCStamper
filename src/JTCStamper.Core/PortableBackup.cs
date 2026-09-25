using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JTCStamper.Core;

// Version 1: fixed KDF parameters, authenticated header, bounded lengths, no DPAPI binding.
public static class PortableBackup
{
    static readonly byte[] Magic = "JTC-PORTABLE-1\n"u8.ToArray();
    static readonly UTF8Encoding Utf8 = new(false, true);
    public const int Iterations = 600_000;
    public const int MaxPlainBytes = 256 * 1024 * 1024;
    const int SaltBytes = 16, NonceBytes = 12, TagBytes = 16;
    static int HeaderBytes => Magic.Length + 4 + SaltBytes + NonceBytes + 4;
    sealed record Payload(int Version, byte[] Key, SignedEntry[] Records);

    public static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length > 1024 ||
            passphrase.EnumerateRunes().Count() < 12)
            throw new ArgumentException("パスフレーズは12文字以上・1024文字以内にしてください。長く推測されにくいものを使ってください。");
        _ = Utf8.GetByteCount(passphrase); // Do not silently replace invalid Unicode.
    }

    public static void Save(string path, string passphrase, byte[] key, IReadOnlyList<SignedEntry> records)
    {
        ValidatePassphrase(passphrase);
        if (key.Length != 32 || records.Any(x => x is null)) throw new InvalidDataException("バックアップの内容が不正です。");
        byte[]? plain = null, derived = null, password = null;
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, key, records.ToArray()));
            if (plain.Length > MaxPlainBytes) throw new InvalidDataException("移行用バックアップは256 MiB以内に対応しています。");
            var header = new byte[HeaderBytes];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(Magic.Length), Iterations);
            var salt = header.AsSpan(Magic.Length + 4, SaltBytes); RandomNumberGenerator.Fill(salt);
            var nonce = header.AsSpan(Magic.Length + 4 + SaltBytes, NonceBytes); RandomNumberGenerator.Fill(nonce);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(HeaderBytes - 4), plain.Length);
            password = Utf8.GetBytes(passphrase);
            derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            var encrypted = new byte[plain.Length]; var tag = new byte[TagBytes];
            using (var aes = new AesGcm(derived, TagBytes)) aes.Encrypt(nonce, plain, encrypted, tag, header);
            AtomicFile.Write(path, stream => { stream.Write(header); stream.Write(encrypted); stream.Write(tag); });
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (derived is not null) CryptographicOperations.ZeroMemory(derived);
            if (password is not null) CryptographicOperations.ZeroMemory(password);
        }
    }

    public static Contents Read(string path, string passphrase)
    {
        ValidatePassphrase(passphrase);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < HeaderBytes + TagBytes || stream.Length > (long)HeaderBytes + TagBytes + MaxPlainBytes)
            throw new InvalidDataException("移行用バックアップのサイズが不正です。");
        var header = new byte[HeaderBytes]; stream.ReadExactly(header);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(Magic.Length)) != Iterations)
            throw new InvalidDataException("対応していない移行用バックアップ形式です。");
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(HeaderBytes - 4));
        if (length <= 0 || length > MaxPlainBytes || stream.Length != (long)HeaderBytes + length + TagBytes)
            throw new InvalidDataException("移行用バックアップの長さが不正です。");
        var encrypted = new byte[length]; var tag = new byte[TagBytes];
        stream.ReadExactly(encrypted); stream.ReadExactly(tag);
        byte[]? password = null, derived = null, plain = null;
        Payload? payload = null;
        try
        {
            password = Utf8.GetBytes(passphrase);
            derived = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(Magic.Length + 4, SaltBytes), Iterations, HashAlgorithmName.SHA256, 32);
            plain = new byte[length];
            try
            {
                using var aes = new AesGcm(derived, TagBytes);
                aes.Decrypt(header.AsSpan(Magic.Length + 4 + SaltBytes, NonceBytes), encrypted, tag, plain, header);
            }
            catch (CryptographicException)
            { throw new InvalidDataException("パスフレーズが違うか、移行用バックアップが破損・改変されています。"); }
            payload = JsonSerializer.Deserialize<Payload>(plain) ?? throw new InvalidDataException("バックアップが空です。");
            if (payload.Version != 1 || payload.Key is null || payload.Key.Length != 32 || payload.Records is null || payload.Records.Any(x => x is null))
                throw new InvalidDataException("バックアップの内容が不正です。");
            var result = new Contents(payload.Key, payload.Records);
            payload = null; // Ownership of the key transfers to the disposable result.
            return result;
        }
        finally
        {
            if (payload?.Key is not null) CryptographicOperations.ZeroMemory(payload.Key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (derived is not null) CryptographicOperations.ZeroMemory(derived);
            if (password is not null) CryptographicOperations.ZeroMemory(password);
        }
    }

    public sealed class Contents : IDisposable
    {
        public byte[] Key { get; }
        public IReadOnlyList<SignedEntry> Records { get; }
        internal Contents(byte[] key, SignedEntry[] records) { Key = key; Records = records; }
        public void Dispose() => CryptographicOperations.ZeroMemory(Key);
    }
}
