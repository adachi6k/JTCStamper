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
    // Cache only parsing/authentication of byte-identical records. Every operation still
    // enumerates and reads every file, hashes its complete bytes and checks the full chain.
    // Timestamps, lengths and file watcher notifications are never integrity evidence.
    readonly object cacheGate = new();
    readonly Dictionary<string, CachedRecord> parsed = new(StringComparer.Ordinal);
    const long CacheBudget = 256L * 1024 * 1024;
    const int MaxCachedRecords = 6000;
    long cacheBytes;
    bool disposed;
    sealed record CachedRecord(byte[] Digest, VerifiedEntry Value, long RetainedBytes);

    VerifiedEntry ReadRecord(string path, bool useCache)
    {
        if (!useCache)
        {
            var uncached = JsonSerializer.Deserialize<SignedEntry>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty record");
            return new(Verify(uncached), uncached);
        }
        var bytes = File.ReadAllBytes(path);
        var digest = SHA256.HashData(bytes);
        lock (cacheGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (parsed.TryGetValue(path, out var cached) && CryptographicOperations.FixedTimeEquals(digest, cached.Digest))
                return cached.Value;
        }
        // Preserve the old reader's BOM handling, including existing UTF-16 records.
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var signed = JsonSerializer.Deserialize<SignedEntry>(reader.ReadToEnd()) ?? throw new InvalidDataException("Empty record");
        var value = new VerifiedEntry(Verify(signed), signed);
        // Conservatively account for both JSON strings and a parsed generation's image string.
        long retained = 2L * (signed.EntryJson.Length + 2L * (value.Entry.Payload?.Length ?? 0) + signed.Mac.Length) + 2048;
        lock (cacheGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (parsed.Remove(path, out var old)) cacheBytes -= old.RetainedBytes;
            if (cacheBytes + retained > CacheBudget) { parsed.Clear(); cacheBytes = 0; }
            if (retained <= CacheBudget) { parsed[path] = new(digest, value, retained); cacheBytes += retained; }
        }
        return value;
    }
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        cancellationToken.ThrowIfCancellationRequested();
        long sequence = 0;
        string previous = "GENESIS";
        var paths = Directory.GetFiles(directory, "*.json");
        bool useCache = paths.Length <= MaxCachedRecords;
        if (!useCache) lock (cacheGate) { parsed.Clear(); cacheBytes = 0; }
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
            var record = ReadRecord(path, useCache);
            var signed = record.Signed; var entry = record.Entry;
            if (entry.Version != 1 || entry.Sequence != ++sequence || entry.PreviousMac != previous ||
                Path.GetFileName(path) != $"{entry.Sequence:D12}.json") throw new InvalidDataException("履歴の順序または連鎖が不正です。");
            previous = signed.Mac;
            yield return record;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    public Entry Verify(SignedEntry signed)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        var actual = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed.EntryJson));
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(signed.Mac)))
            throw new InvalidDataException("履歴のHMAC検証に失敗しました。");
        return JsonSerializer.Deserialize<Entry>(signed.EntryJson) ?? throw new InvalidDataException("Empty entry");
    }
    public SignedEntry Append(string kind, Guid eventId, object payload)
    {
        // Verify every record, but retain only the chain tail when appending.
        var last = ReadVerified().LastOrDefault();
        return WriteAfter(last, kind, eventId, payload).Signed;
    }
    // One operation's completion snapshot: never reused for a later user action.
    // The same full byte/chain verification runs before writing CopyCompleted.
    internal VerifiedHistory CompleteCopyAndCapture(Guid eventId, object payload)
    {
        var records = ReadVerified().ToList();
        records.Add(WriteAfter(records.LastOrDefault(), "CopyCompleted", eventId, payload));
        return VerificationService.FromVerifiedEntries(records);
    }
    VerifiedEntry WriteAfter(VerifiedEntry? last, string kind, Guid eventId, object payload)
    {
        var entry = new Entry(1, (last?.Entry.Sequence ?? 0) + 1, last?.Signed.Mac ?? "GENESIS",
            kind, eventId, DateTimeOffset.UtcNow, JsonSerializer.Serialize(payload));
        var json = JsonSerializer.Serialize(entry);
        var signed = new SignedEntry(json, Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json))));
        string target = Path.Combine(directory, $"{entry.Sequence:D12}.json");
        AtomicFile.Write(target, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(signed)), overwrite: false);
        return new(entry, signed);
    }
    // Delete newest first: an interrupted deletion leaves a valid prefix, never a broken chain.
    // Keep the owner lock and key; unrelated files are never removed.
    public void DeleteAllHistory()
    {
        var records = Read();
        try
        {
            foreach (var record in records.Reverse())
                File.Delete(Path.Combine(directory, $"{record.Entry.Sequence:D12}.json"));
        }
        finally { lock (cacheGate) { parsed.Clear(); cacheBytes = 0; } }
    }
    public void Annotate(Guid eventId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("注釈は1〜2000文字です。");
        if (!Read().Any(x => x.Entry.Kind == "Generated" && x.Entry.EventId == eventId)) throw new InvalidDataException("生成履歴がありません。");
        Append("AnnotationAdded", eventId, new { Text = text });
    }
    public void Dispose()
    {
        if (Volatile.Read(ref disposed)) return;
        Volatile.Write(ref disposed, true);
        owner.Dispose(); CryptographicOperations.ZeroMemory(key);
        lock (cacheGate) { parsed.Clear(); cacheBytes = 0; }
    }
}

public interface IClipboard { void Copy(byte[] png); }
public sealed class CopyService(IJournalWriter journal, IClipboard clipboard)
{
    public VerifiedHistory? CompletedHistory { get; private set; }
    public Guid GenerateCodedAndCopy(Stamp stamp, Func<Stamp, byte[]> render, bool captureHistory = false)
    {
        CompletedHistory = null;
        var id = Guid.NewGuid();
        if (stamp.Renderer != RingCode.Renderer && stamp.Renderer != RingCode.PlainRenderer) throw new NotSupportedException("対応していない印影形式です。");
        var coded = stamp with { GeometryCode = stamp.Renderer == RingCode.PlainRenderer ? null : RingCode.ForEvent(id) };
        RingCode.Validate(coded);
        var png = render(coded);
        return SaveAndCopy(id, coded, png, captureHistory);
    }
    Guid SaveAndCopy(Guid id, Stamp stamp, byte[] png, bool captureHistory)
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
        var completed = new { Format = "PNG", Meaning = "Clipboard API completed; paste unobserved" };
        if (captureHistory && journal is Journal owned) CompletedHistory = owned.CompleteCopyAndCapture(id, completed);
        else journal.Append("CopyCompleted", id, completed);
        return id;
    }
}
