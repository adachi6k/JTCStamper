using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using JTCStamper.Core;

namespace JTCStamper.App;

internal static class HistoryImage
{
    public static (BitmapSource? Image, string Description) Load(Generation generation)
    {
        var stored = VerificationService.StoredPng(generation);
        if (stored is not null)
        {
            using var stream = new MemoryStream(stored);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return (frame, "生成時の保存画像");
        }
        var image = StampRenderer.Render(generation.Stamp);
        var hash = Convert.ToHexString(SHA256.HashData(StampRenderer.Png(image)));
        return hash.Equals(generation.PngSha256, StringComparison.OrdinalIgnoreCase)
            ? (image, "再現画像（生成時のPNGとハッシュ一致）")
            : (null, "画像未保存・生成時の画像を再現できません");
    }
}
