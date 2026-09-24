using System.Text.Json;
namespace JTCStamper.Core;

// Editable stamp settings are not authenticated originals and contain no secret key.
public sealed record StampSettings(int Version, string Name, DateOnly DisplayDate, string Bottom, bool Plain = false)
{
    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("未対応の設定ファイル形式です。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 16 ||
            string.IsNullOrWhiteSpace(Bottom) || Bottom.Length > 16)
            throw new InvalidDataException("上段文字と下段文字は1〜16文字で指定してください。");
    }
    public static StampSettings Load(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("設定ファイルが大きすぎます。");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var field in new[] { "Version", "Name", "DisplayDate", "Bottom" })
            if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("設定項目が不足しています: " + field);
        var value = document.Deserialize<StampSettings>() ?? throw new InvalidDataException("設定ファイルが空です。");
        value.Validate(); return value;
    }
    public void Save(string path)
    {
        Validate();
        AtomicFile.Write(path, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(this,
            new JsonSerializerOptions { WriteIndented = true })));
    }
}
