using System.Text.Json;

namespace JTCStamper.Core;

public static class GenerationContext
{
    public static string Purpose(Generation generation) => string.IsNullOrWhiteSpace(generation.InitialAnnotation)
        ? "用途メモなし" : "用途：" + generation.InitialAnnotation;
    public static string Summary(Generation generation)
    {
        var text = string.Join(" ", Purpose(generation).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 120 ? text : text[..120] + "…";
    }
    public static string Describe(Generation generation) =>
        $"実生成：{generation.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}\n{Purpose(generation)}\n" +
        (generation.DateMode == "Specified" ? "日付指定で生成" : generation.DateMode == "Today" ? "当日モードで生成" : "日付モードの記録なし");
    public static string FollowUps(Guid id, IReadOnlyList<VerifiedEntry> entries) => string.Join("\n", entries
        .Where(x => x.Entry.EventId == id && x.Entry.Kind == "AnnotationAdded")
        .Select(x => $"追記 {x.Entry.RecordedUtc.ToLocalTime():yyyy/MM/dd HH:mm}：" +
            AnnotationText(x.Entry.Payload)));
    static string? AnnotationText(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("Text").GetString();
    }
    public static string CopyState(Guid id, IReadOnlyList<VerifiedEntry> entries) =>
        entries.Any(x => x.Entry.EventId == id && x.Entry.Kind == "CopyCompleted") ? "コピー：完了" :
        entries.Any(x => x.Entry.EventId == id && x.Entry.Kind == "CopyFailed") ? "コピー：失敗" : "コピー：未確認";
}
