using System.IO;
using System.Text.Json;
using System.Windows;

namespace JTCStamper.App;

internal enum AppearanceMode { System, Light, Dark }
internal static class Appearance
{
    public static AppearanceMode Current { get; private set; } = AppearanceMode.System;
    static string FilePath(string root) => Path.Combine(root, "appearance.json");
    public static AppearanceMode Load(string root)
    {
        if (!File.Exists(FilePath(root))) return AppearanceMode.System;
        var value = JsonSerializer.Deserialize<Preference>(File.ReadAllText(FilePath(root)));
        if (value is null || value.Version != 1 || !Enum.TryParse<AppearanceMode>(value.Theme, out var mode) || !Enum.IsDefined(mode))
            throw new InvalidDataException("外観設定の形式を確認できません。");
        return mode;
    }
    public static void Save(string root, AppearanceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Directory.CreateDirectory(root);
        string temporary = Path.Combine(root, $"appearance-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(1, mode.ToString())));
            File.Move(temporary, FilePath(root), true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Apply(AppearanceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        // .NET's built-in Fluent API is experimental; isolate that dependency here.
#pragma warning disable WPF0001
        Application.Current.ThemeMode = mode switch
        {
            AppearanceMode.Light => ThemeMode.Light,
            AppearanceMode.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };
#pragma warning restore WPF0001
        Current = mode;
    }
    sealed record Preference(int Version, string Theme);
}
