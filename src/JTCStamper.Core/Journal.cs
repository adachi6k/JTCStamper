using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JTCStamper.Core;

public sealed record Stamp(string Name, DateOnly DisplayDate, string Bottom, string Renderer = "wpf-v1", int? GeometryCode = null);
public sealed record Generation(Guid EventId, DateTimeOffset CreatedUtc, Stamp Stamp, string PngSha256);
public sealed record Entry(int Version, long Sequence, string PreviousMac, string Kind, Guid EventId,
    DateTimeOffset RecordedUtc, string Payload);
public sealed record SignedEntry(string EntryJson, string Mac);
public sealed record VerifiedEntry(Entry Entry, SignedEntry Signed);

// One process owns a journal for its lifetime. Entries are immutable, flushed then atomically renamed.
public sealed class Journal : IDisposable
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
        try { Read(); } catch { owner.Dispose(); CryptographicOperations.ZeroMemory(this.key); throw; }
    }
    public IReadOnlyList<VerifiedEntry> Read()
    {
        var result = new List<VerifiedEntry>();
        string previous = "GENESIS";
        foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var signed = JsonSerializer.Deserialize<SignedEntry>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty record");
            var entry = Verify(signed);
            if (entry.Version != 1 || entry.Sequence != result.Count + 1 || entry.PreviousMac != previous ||
                Path.GetFileName(path) != $"{entry.Sequence:D12}.json") throw new InvalidDataException("履歴の順序または連鎖が不正です。");
            result.Add(new(entry, signed)); previous = signed.Mac;
        }
        return result;
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
        var history = Read();
        var entry = new Entry(1, history.Count + 1, history.LastOrDefault()?.Signed.Mac ?? "GENESIS",
            kind, eventId, DateTimeOffset.UtcNow, JsonSerializer.Serialize(payload));
        var json = JsonSerializer.Serialize(entry);
        var signed = new SignedEntry(json, Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json))));
        string target = Path.Combine(directory, $"{entry.Sequence:D12}.json");
        string temp = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(signed)); stream.Flush(true);
            }
            File.Move(temp, target, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return signed;
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
public sealed class CopyService(Journal journal, IClipboard clipboard)
{
    public Guid GenerateAndCopy(Stamp stamp, byte[] png)
    {
        GeometryCode.Validate(stamp);
        if (stamp.GeometryCode is not null) throw new ArgumentException("符号付きの生成には描画コールバックを使用してください。");
        return SaveAndCopy(Guid.NewGuid(), stamp, png);
    }
    public Guid GenerateCodedAndCopy(Stamp stamp, Func<Stamp, byte[]> render)
    {
        var id = Guid.NewGuid();
        var coded = stamp with { Renderer = RingCode.Renderer, GeometryCode = RingCode.ForEvent(id) };
        var png = render(coded);
        return SaveAndCopy(id, coded, png);
    }
    Guid SaveAndCopy(Guid id, Stamp stamp, byte[] png)
    {
        var generation = new Generation(id, DateTimeOffset.UtcNow, stamp, Convert.ToHexString(SHA256.HashData(png)));
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
