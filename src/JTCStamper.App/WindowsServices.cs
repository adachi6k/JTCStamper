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
        var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
        AtomicFile.Write(path, stream => stream.Write(protectedKey), overwrite: false);
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

public sealed record ClipboardImage(byte[]? Png, BitmapSource? Bitmap);

public static class ClipboardImages
{
    const int MaxBytes = 32 * 1024 * 1024;
    // OLE can advertise a format before its payload is readable. Retry reads only;
    // never replace the user's clipboard or treat a missing payload as a valid image.
    public static async Task<ClipboardImage?> ReadAsync(bool requireBitmap = false)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                bool hasPng = data?.GetDataPresent("PNG") == true;
                bool hasBitmap = data?.GetDataPresent(DataFormats.Bitmap) == true;
                if (data is not null && !hasPng && !hasBitmap) return null;
                byte[]? png = null;
                if (hasPng)
                {
                    var payload = data!.GetData("PNG");
                    if (payload is byte[] array)
                    {
                        if (array.Length > MaxBytes) throw new InvalidDataException("画像は32MiB以内にしてください。");
                        png = array;
                    }
                    else if (payload is Stream source)
                    {
                        if (source.CanSeek) source.Position = 0;
                        using var copy = new MemoryStream();
                        var buffer = new byte[81920]; int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (copy.Length + read > MaxBytes) throw new InvalidDataException("画像は32MiB以内にしてください。");
                            copy.Write(buffer, 0, read);
                        }
                        if (copy.Length > 0) png = copy.ToArray();
                    }
                }
                var bitmap = hasBitmap ? data!.GetData(DataFormats.Bitmap) as BitmapSource : null;
                if (bitmap is not null) bitmap.Freeze();
                if ((!hasPng || png is not null) && (!requireBitmap || bitmap is not null) && (png is not null || bitmap is not null))
                    return new(png, bitmap);
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 9) { }
            if (attempt < 9) await Task.Delay(80);
        }
        throw new IOException("クリップボードの画像を読み取れませんでした。少し待って、もう一度お試しください。");
    }
}
