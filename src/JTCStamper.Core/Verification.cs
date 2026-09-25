using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace JTCStamper.Core;

public enum VerificationStatus { Match, NoRecord, Indeterminate }
public sealed record VerificationResult(VerificationStatus Status, string Explanation, IReadOnlyList<Generation> Matches)
{
    public string Label => Status switch
    {
        VerificationStatus.Match => "一致する生成履歴あり",
        VerificationStatus.NoRecord => "一致記録なし",
        _ => "判定不能"
    };
}

public sealed record VerifiedHistory(IReadOnlyList<VerifiedEntry> Entries, IReadOnlyList<Generation> Generations);

// Read-only exact matching. An image match never identifies a unique event by itself.
public sealed class VerificationService(Journal journal)
{
    public VerificationResult Image(byte[] bytes, CancellationToken cancellationToken = default) => Guard(() =>
    {
        var history = Generations(cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var matches = history.Where(x => x.Generation.PngSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Generation).ToArray();
        return new(matches.Length > 0 ? VerificationStatus.Match : VerificationStatus.NoRecord,
            matches.Length > 0 ? "保存されたPNGとファイル全体が完全一致しました。同じ印影の履歴が複数ある場合、画像からイベントを特定できません。"
            : "この保存先にファイル全体が完全一致するPNGの記録はありません。加工・再保存された画像や別の保存先の履歴は、この方法では照合できません。偽造を意味しません。", matches);
    });

    public VerificationResult Original(string json, CancellationToken cancellationToken = default) => Guard(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > 1024 * 1024) throw new InvalidDataException();
        var signed = JsonSerializer.Deserialize<SignedEntry>(json) ?? throw new InvalidDataException();
        var entry = journal.Verify(signed);
        var generation = Parse(entry);
        var history = Generations(cancellationToken);
        var sameId = history.Where(x => x.Generation.EventId == generation.EventId).ToArray();
        if (sameId.Length == 0)
            return new(VerificationStatus.NoRecord, "原本の認証情報は検証できましたが、この保存先には該当イベントの生成記録がありません。偽造を意味しません。", []);
        if (sameId.Length != 1 || sameId[0].Signed.EntryJson != signed.EntryJson)
            return new(VerificationStatus.Indeterminate, "同じイベントIDの記録と原本の内容が矛盾しています。", []);
        return new(VerificationStatus.Match, "原本の認証情報・イベントID・生成記録の内容が一致しました。貼付完了や画像の利用者を証明するものではありません。", [generation]);
    });

    public IReadOnlyList<Generation> ReadGenerations(CancellationToken cancellationToken = default) => Generations(cancellationToken).Select(x => x.Generation).ToArray();

    // A fresh authenticated snapshot per operation; Journal re-reads every file even on cache hits.
    public VerifiedHistory ReadHistory()
    {
        var entries = journal.Read();
        return new(entries, Generations(entries).Select(x => x.Generation).ToArray());
    }

    // Same semantic validation as ReadHistory, including other current-format generations,
    // without retaining their embedded images. Publish only after the entire scan succeeds.
    public VerifiedHistory ReadEventHistory(Guid eventId, CancellationToken cancellationToken = default)
    {
        var entries = new List<VerifiedEntry>();
        var generations = new List<Generation>();
        foreach (var record in journal.ReadVerified(cancellationToken))
        {
            Generation? generation = CurrentGeneration(record.Entry);
            if (record.Entry.EventId != eventId) continue;
            entries.Add(record);
            if (generation is not null) generations.Add(generation);
        }
        return new(entries, generations);
    }

    List<(Generation Generation, SignedEntry Signed)> Generations(CancellationToken cancellationToken = default) =>
        Generations(journal.ReadVerified(cancellationToken).ToArray(), cancellationToken);
    static List<(Generation Generation, SignedEntry Signed)> Generations(IReadOnlyList<VerifiedEntry> entries, CancellationToken cancellationToken = default)
    {
        var result = new List<(Generation, SignedEntry)>();
        foreach (var record in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentGeneration(record.Entry) is Generation generation) result.Add((generation, record.Signed));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    // Entries are immutable and returned only after Journal has checked the current file bytes.
    // Weak keys keep this semantic cache bounded by live snapshots and Journal's parsing cache.
    sealed record ParsedGeneration(Generation? Value);
    static readonly ConditionalWeakTable<Entry, ParsedGeneration> generationCache = new();
    static Generation? CurrentGeneration(Entry entry) => generationCache.GetValue(entry,
        e => new(e.Kind == "Generated" && IsCurrent(e) ? Parse(e) : null)).Value;

    static bool IsCurrent(Entry entry)
    {
        using var document = JsonDocument.Parse(entry.Payload);
        return document.RootElement.TryGetProperty("Stamp", out var stamp) &&
            stamp.TryGetProperty("Renderer", out var renderer) && renderer.GetString() is RingCode.Renderer or RingCode.PlainRenderer;
    }

    static Generation Parse(Entry entry)
    {
        if (entry.Version != 1 || entry.Kind != "Generated" || entry.EventId == Guid.Empty)
            throw new InvalidDataException();
        if (!IsCurrent(entry)) throw new NotSupportedException("対応していない印影形式です。現在の12ビット形式とプレーン形式を照合できます。");
        var g = JsonSerializer.Deserialize<Generation>(entry.Payload) ?? throw new InvalidDataException();
        if (g.EventId != entry.EventId || g.Stamp is null || g.PngSha256 is null ||
            g.PngSha256.Length != 64 || !g.PngSha256.All(Uri.IsHexDigit)) throw new InvalidDataException();
        RingCode.Validate(g.Stamp);
        if (g.Stamp.GeometryCode is int code && RingCode.ForEvent(g.EventId) != code) throw new InvalidDataException("イベントと幾何コードが矛盾しています。");
        StoredPng(g);
        return g;
    }
    public static byte[]? StoredPng(Generation generation)
    {
        if (generation.PngBase64 is null) return null;
        var bytes = Convert.FromBase64String(generation.PngBase64);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(generation.PngSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("保存した印影画像が生成記録と一致しません。");
        return bytes;
    }
    static VerificationResult Guard(Func<VerificationResult> action)
    {
        try { return action(); }
        catch (NotSupportedException ex) { return new(VerificationStatus.Indeterminate, ex.Message, []); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or CryptographicException)
        {
            return new(VerificationStatus.Indeterminate, "原本または履歴を検証できませんでした。別PC・別の鍵、ファイルの破損、読み取りエラーなどが考えられます。", []);
        }
    }
}
