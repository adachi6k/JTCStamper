using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.Core;
namespace JTCStamper.App;
public static class StampRenderer
{
    public static BitmapSource Render(Stamp stamp)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var red = new SolidColorBrush(Color.FromRgb(195, 32, 40));
            var pen = new Pen(red, 1.1);
            // Design coordinates: 96 units. Circle, separators and text are independent primitives.
            dc.DrawEllipse(null, pen, new Point(48, 48), 43, 43);
            foreach (double y in new[] { 33.0, 63.0 })
            {
                double dx = Math.Sqrt(43 * 43 - (y - 48) * (y - 48));
                dc.DrawLine(pen, new Point(48 - dx, y), new Point(48 + dx, y));
            }
            DrawText(dc, stamp.Name, 21, 54, 17, red);
            DrawText(dc, stamp.DisplayDate.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture), 48, 77, 13, red);
            DrawText(dc, stamp.Bottom, 75, 54, 16, red);
        }
        var bitmap = new RenderTargetBitmap(384, 384, 384, 384, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    static void DrawText(DrawingContext dc, string value, double cy, double width, double size, Brush brush)
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
            new Typeface("Yu Gothic"), size, brush, 1);
        if (text.Width > width) text.SetFontSize(size * width / text.Width);
        dc.DrawText(text, new Point(48 - text.Width / 2, cy - text.Height / 2));
    }
    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
