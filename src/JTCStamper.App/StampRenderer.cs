using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.Core;
namespace JTCStamper.App;
public static class StampRenderer
{
    // Provisional aesthetic treatment: keep removable independently of the 20bit layout.
    private const double GapInkOpacity = 0.10;
    private static readonly ImageBrush InkOpacity = CreateInkOpacity();

    public static BitmapSource Render(Stamp stamp) => Render(stamp, new Typeface("Yu Gothic"));

    // Internal fixture seam: reproduce historical font differences without changing the product font.
    internal static BitmapSource Render(Stamp stamp, Typeface typeface)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(CreateDrawing(stamp, typeface));
        var bitmap = new RenderTargetBitmap(384, 384, 384, 384, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    // Shared vector scene; PNG and experimental EMF consume the same geometry and glyph outlines.
    internal static DrawingGroup CreateDrawing(Stamp stamp, Typeface typeface)
    {
        RingCode.Validate(stamp);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            // One fixed decorative texture, independent of the event/code: no extra identifier.
            // Keep plain output unchanged; apply only to the current coded design.
            bool textured = stamp.Renderer == RingCode20.Renderer;
            if (textured) dc.PushOpacityMask(InkOpacity);
            var red = new SolidColorBrush(Color.FromRgb(195, 32, 40));
            var pen = new Pen(red, 1.1);
            var gapBrush = new SolidColorBrush(red.Color) { Opacity = GapInkOpacity };
            var gapPen = new Pen(gapBrush, 1.1);
            // Design coordinates: 96 units. Circle, separators and text are independent primitives.
            var gaps = stamp.Renderer == RingCode.PlainRenderer ? Array.Empty<bool>() : RingCode.Encode(stamp.GeometryCode!.Value);
            double start = 0;
            for (int i = 0; i < (stamp.Renderer == RingCode20.Renderer ? RingCode20.RingCells : gaps.Length); i++) if (gaps[i])
            {
                double center = RingCode.CellAngle(i);
                DrawArc(dc, pen, start, center - RingCode.GapDegrees / 2);
                DrawArc(dc, gapPen, center - RingCode.GapDegrees / 2, center + RingCode.GapDegrees / 2);
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
                Point At(double t) => new(48 + t * cosine, y + t * sine);
                double from = t0;
                if(stamp.Renderer == RingCode20.Renderer)
                    for(int j=0;j<2;j++)
                    {
                        int row = y < 48 ? 0 : 1;
                        if(!gaps[RingCode20.RingCells+row*2+j]) continue;
                        double t = j == 0 ? -24 : 24;
                        dc.DrawLine(pen,At(from),At(t-1.6));
                        dc.DrawLine(gapPen, At(t-1.6), At(t+1.6));
                        from=t+1.6;
                    }
                dc.DrawLine(pen, At(from), At(t1));
            }
            DrawText(dc, stamp.Name, 22, 24, red, typeface);
            DrawText(dc, "\'" + stamp.DisplayDate.ToString("yy.MM.dd", CultureInfo.InvariantCulture), 48, 24, red, typeface);
            DrawText(dc, stamp.Bottom, 74, 24, red, typeface);
            if (textured) dc.Pop();
        }
        drawing.Freeze();
        return drawing;
    }
    private static ImageBrush CreateInkOpacity()
    {
        // Bilinear interpolation of coarse and fine opacity fields. Fixed PRNG avoids
        // runtime-dependent Random sequences; overall opacity stays between ~80% and 100%.
        uint state = 0x4A544331;
        byte[] Field(int size, int minimum)
        {
            var values = new byte[size * size];
            for (int i = 0; i < values.Length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                values[i] = (byte)(minimum + state % (uint)(256 - minimum));
            }
            return values;
        }
        const int resolution = 384;
        double Sample(byte[] field, int size, int x, int y)
        {
            double u = Math.Clamp((x + .5) * size / resolution - .5, 0, size - 1);
            double v = Math.Clamp((y + .5) * size / resolution - .5, 0, size - 1);
            int left = (int)u, top = (int)v;
            int right = Math.Min(left + 1, size - 1), bottom = Math.Min(top + 1, size - 1);
            double fx = u - left, fy = v - top;
            double a = field[top * size + left] * (1 - fx) + field[top * size + right] * fx;
            double b = field[bottom * size + left] * (1 - fx) + field[bottom * size + right] * fx;
            return a * (1 - fy) + b * fy;
        }
        var coarse = Field(24, 224);
        var fine = Field(192, 232);
        var pixels = new byte[resolution * resolution * 4];
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                byte alpha = (byte)Math.Round(Sample(coarse, 24, x, y) * Sample(fine, 192, x, y) / 255);
                int offset = (y * resolution + x) * 4;
                // Premultiplied white: only alpha is used by the opacity mask.
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = alpha;
            }
        var image = BitmapSource.Create(resolution, resolution, 384, 384,
            PixelFormats.Pbgra32, null, pixels, resolution * 4);
        image.Freeze();
        var brush = new ImageBrush(image)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 96, 96),
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
        brush.Freeze();
        return brush;
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
