using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.Core;
namespace JTCStamper.App;
public static class StampRenderer
{
    public static BitmapSource Render(Stamp stamp) => Render(stamp, new Typeface("Yu Gothic"));

    // Internal fixture seam: reproduce historical font differences without changing the product font.
    internal static BitmapSource Render(Stamp stamp, Typeface typeface)
    {
        RingCode.Validate(stamp);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var red = new SolidColorBrush(Color.FromRgb(195, 32, 40));
            var pen = new Pen(red, 1.1);
            // Design coordinates: 96 units. Circle, separators and text are independent primitives.
            var gaps = stamp.Renderer == RingCode.PlainRenderer ? Array.Empty<bool>() : RingCode.Encode(stamp.GeometryCode!.Value);
            double start = 0;
            for (int i = 0; i < gaps.Length; i++) if (gaps[i])
            {
                double center = RingCode.CellAngle(i);
                DrawArc(dc, pen, start, center - RingCode.GapDegrees / 2);
                start = center + RingCode.GapDegrees / 2;
            }
            if (stamp.Renderer == RingCode.PlainRenderer) dc.DrawEllipse(null, pen, new Point(48, 48), 43, 43);
            else DrawArc(dc, pen, start, 360);
            foreach (double y in new[] { 33.0, 63.0 })
            {
                double difference = stamp.Renderer == RingCode.PlainRenderer ? 0 : RingCode.SeparatorDifference(stamp.GeometryCode!.Value);
                double angle = (y < 48 ? difference / 2 : -difference / 2) * Math.PI / 180;
                double cosine = Math.Cos(angle), sine = Math.Sin(angle), offset = y - 48;
                double half = Math.Sqrt(43 * 43 - offset * offset * cosine * cosine);
                double t0 = -offset * sine - half, t1 = -offset * sine + half;
                dc.DrawLine(pen, new Point(48 + t0 * cosine, y + t0 * sine), new Point(48 + t1 * cosine, y + t1 * sine));
            }
            DrawText(dc, stamp.Name, 22, 24, red, typeface);
            DrawText(dc, "\'" + stamp.DisplayDate.ToString("yy.MM.dd", CultureInfo.InvariantCulture), 48, 24, red, typeface);
            DrawText(dc, stamp.Bottom, 74, 24, red, typeface);
        }
        var bitmap = new RenderTargetBitmap(384, 384, 384, 384, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    static void DrawArc(DrawingContext dc, Pen pen, double startDegrees, double endDegrees)
    {
        if (endDegrees <= startDegrees) return;
        Point At(double degrees) => new(48 + 43 * Math.Cos(degrees * Math.PI / 180), 48 + 43 * Math.Sin(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(startDegrees), false, false);
            context.ArcTo(At(endDegrees), new Size(43, 43), 0, endDegrees - startDegrees > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
    }
    static void DrawText(DrawingContext dc, string value, double cy, double maxHeight, Brush brush, Typeface typeface)
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
            typeface, 100, brush, 1);
        var outline = text.BuildGeometry(new Point(0, 0));
        var bounds = outline.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        // Fit visible glyph bounds, not font line-height, inside the circle and separator bands.
        double lo = 0, hi = maxHeight / bounds.Height;
        for (int i = 0; i < 48; i++)
        {
            double scale = (lo + hi) / 2;
            double x = bounds.Width * scale / 2;
            double height = bounds.Height * scale;
            double center = cy < 48 ? 30 - height / 2 : cy > 48 ? 66 + height / 2 : 48;
            double y = Math.Abs(center - 48) + height / 2;
            if (x * x + y * y <= 41 * 41) lo = scale; else hi = scale;
        }
        cy = cy < 48 ? 30 - bounds.Height * lo / 2 : cy > 48 ? 66 + bounds.Height * lo / 2 : 48;
        outline.Transform = new MatrixTransform(lo, 0, 0, lo,
            48 - (bounds.X + bounds.Width / 2) * lo,
            cy - (bounds.Y + bounds.Height / 2) * lo);
        dc.DrawGeometry(brush, null, outline);
    }
    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
