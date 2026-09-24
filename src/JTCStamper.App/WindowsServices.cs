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
    public void Copy(byte[] png) => Clipboard.SetDataObject(CreateData(png), true);

    internal static DataObject CreateData(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        var data = new DataObject();
        // The data object may still serve managed/OLE readers after this call returns.
        // It owns this managed stream; disposing it here makes later PNG reads unreliable.
        data.SetData("PNG", new MemoryStream(png, writable: false), autoConvert: false);
        data.SetImage(bitmap);
        return data;
    }
}

public sealed record ClipboardImage(byte[]? Png, BitmapSource? Bitmap);

public static class ClipboardImages
{
    // OLE can advertise a format before its payload is readable. Retry reads only;
    // never replace the user's clipboard or treat a missing payload as a valid image.
    public static async Task<ClipboardImage?> ReadAsync(bool requireBitmap = false)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                // Read PNG bytes while the Windows clipboard and its memory are locked.
                // WPF/OLE returned corrupted payloads intermittently in real-machine testing,
                // while the native PNG remained byte-identical in the same clipboard sequence.
                var native = NativeClipboard.ReadPng();
                bool hasPng = native.Available;
                byte[]? png = native.Bytes;
                if (png is not null && !requireBitmap) return new(png, null);
                var data = Clipboard.GetDataObject();
                bool hasBitmap = data?.GetDataPresent(DataFormats.Bitmap) == true;
                if (data is not null && !hasPng && !hasBitmap) return null;
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
