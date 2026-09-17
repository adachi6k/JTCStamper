using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using JTCStamper.Core;
namespace JTCStamper.App;
public static class KeyStore
{
    public static byte[] Load(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "key.dpapi");
        if (File.Exists(path)) return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        if (Directory.Exists(Path.Combine(root, "journal")) && Directory.EnumerateFiles(Path.Combine(root, "journal"), "*.json").Any())
            throw new InvalidDataException("秘密鍵がありません。鍵を再作成せず、バックアップから復旧してください。");
        var key = RandomNumberGenerator.GetBytes(32);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)); output.Flush(true);
        return key;
    }
}
public sealed class WindowsClipboard : IClipboard
{
    public void Copy(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        var data = new DataObject();
        using var pngStream = new MemoryStream(png);
        data.SetData("PNG", pngStream);
        data.SetImage(bitmap);
        Clipboard.SetDataObject(data, true);
    }
}
