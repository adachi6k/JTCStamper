using System.Reflection;
namespace JTCStamper.App;

internal static class BuildIdentity
{
    internal static string Description
    {
        get
        {
            var assembly = typeof(BuildIdentity).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "不明";
            var edition = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "Distribution")?.Value;
            return $"バージョン：{version}\n配布形式：{(edition == "Standard" ? "通常版" : "軽量版／開発ビルド")}";
        }
    }
}
