using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JTCStamper.Core;

public sealed record Stamp(string Name, DateOnly DisplayDate, string Bottom, string Renderer = RingCode.Renderer, int? GeometryCode = null);
public sealed record Generation(Guid EventId, DateTimeOffset CreatedUtc, Stamp Stamp, string PngSha256, string? PngBase64 = null);
public sealed record Entry(int Version, long Sequence, string PreviousMac, string Kind, Guid EventId,
    DateTimeOffset RecordedUtc, string Payload);
public sealed record SignedEntry(string EntryJson, string Mac);
public sealed record VerifiedEntry(Entry Entry, SignedEntry Signed);

// Append-only boundary used by the copy workflow; Journal remains the authenticated implementation.
public interface IJournalWriter
{
    SignedEntry Append(string kind, Guid eventId, object payload);
}

// One process owns a journal for its lifetime. Entries are immutable, flushed then atomically renamed.
public sealed class Journal : IDisposable, IJournalWriter
{
    readonly string directory;
    readonly byte[] key;
    readonly FileStream owner;
    public Journal(string directory, byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("A 256-bit key is required.");
        this.directory = directory; this.key = key.ToArray();
        Directory.CreateDirectory(directory);
        owner = new FileStream(Path.Combine(directory, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { foreach (var _ in ReadVerified()) { } } catch { owner.Dispose(); CryptographicOperations.ZeroMemory(this.key); throw; }
    }
    public IReadOnlyList<VerifiedEntry> Read() => ReadVerified().ToArray();
    internal IEnumerable<VerifiedEntry> ReadVerified(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long sequence = 0;
        string previous = "GENESIS";
        foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signed = JsonSerializer.Deserialize<SignedEntry>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty record");
            var entry = Verify(signed);
            if (entry.Version != 1 || entry.Sequence != ++sequence || entry.PreviousMac != previous ||
                Path.GetFileName(path) != $"{entry.Sequence:D12}.json") throw new InvalidDataException("履歴の順序または連鎖が不正です。");
            previous = signed.Mac;
            yield return new(entry, signed);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    public Entry Verify(SignedEntry signed)
    {
        var actual = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed.EntryJson));
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(signed.Mac)))
            throw new InvalidDataException("履歴のHMAC検証に失敗しました。");
        return JsonSerializer.Deserialize<Entry>(signed.EntryJson) ?? throw new InvalidDataException("Empty entry");
    }
    public SignedEntry Append(string kind, Guid eventId, object payload)
    {
        // Verify every record, but retain only the chain tail when appending.
        var last = ReadVerified().LastOrDefault();
        var entry = new Entry(1, (last?.Entry.Sequence ?? 0) + 1, last?.Signed.Mac ?? "GENESIS",
            kind, eventId, DateTimeOffset.UtcNow, JsonSerializer.Serialize(payload));
        var json = JsonSerializer.Serialize(entry);
        var signed = new SignedEntry(json, Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json))));
        string target = Path.Combine(directory, $"{entry.Sequence:D12}.json");
        AtomicFile.Write(target, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(signed)), overwrite: false);
        return signed;
    }
    // Delete newest first: an interrupted deletion leaves a valid prefix, never a broken chain.
    // Keep the owner lock and key; unrelated files are never removed.
    public void DeleteAllHistory()
    {
        var records = Read();
        foreach (var record in records.Reverse())
            File.Delete(Path.Combine(directory, $"{record.Entry.Sequence:D12}.json"));
    }
    public void Annotate(Guid eventId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("注釈は1〜2000文字です。");
        if (!Read().Any(x => x.Entry.Kind == "Generated" && x.Entry.EventId == eventId)) throw new InvalidDataException("生成履歴がありません。");
        Append("AnnotationAdded", eventId, new { Text = text });
    }
    public void Dispose() { owner.Dispose(); CryptographicOperations.ZeroMemory(key); }
}

public interface IClipboard { void Copy(byte[] png); }
public sealed class CopyService(IJournalWriter journal, IClipboard clipboard)
{
    public Guid GenerateCodedAndCopy(Stamp stamp, Func<Stamp, byte[]> render)
    {
        var id = Guid.NewGuid();
        if (stamp.Renderer != RingCode.Renderer && stamp.Renderer != RingCode.PlainRenderer) throw new NotSupportedException("対応していない印影形式です。");
        var coded = stamp with { GeometryCode = stamp.Renderer == RingCode.PlainRenderer ? null : RingCode.ForEvent(id) };
        RingCode.Validate(coded);
        var png = render(coded);
        return SaveAndCopy(id, coded, png);
    }
    Guid SaveAndCopy(Guid id, Stamp stamp, byte[] png)
    {
        var generation = new Generation(id, DateTimeOffset.UtcNow, stamp, Convert.ToHexString(SHA256.HashData(png)), Convert.ToBase64String(png));
        journal.Append("Generated", id, generation);
        journal.Append("CopyRequested", id, new { Format = "PNG" });
        try { clipboard.Copy(png); }
        catch (Exception ex)
        {
            journal.Append("CopyFailed", id, new { ErrorType = ex.GetType().Name });
            throw;
        }
        // If this fails, the caller must show failure/unknown, even though clipboard may contain the image.
        journal.Append("CopyCompleted", id, new { Format = "PNG", Meaning = "Clipboard API completed; paste unobserved" });
        return id;
    }
}
