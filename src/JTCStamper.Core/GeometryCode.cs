using System.Security.Cryptography;

namespace JTCStamper.Core;

public sealed record GeometryReading(int? Code, double? DifferenceDegrees, double? RotationDegrees, string Reason);

public static class GeometryCode
{
    public const string Renderer = "wpf-v3-angle2";
    public static int ForEvent(Guid eventId) => SHA256.HashData(eventId.ToByteArray())[0] & 3;
    public static double Difference(int code) => code is >= 0 and <= 3 ? code * 2 - 3 : throw new ArgumentOutOfRangeException(nameof(code));
    public static string Label(int code) => Convert.ToString(code, 2).PadLeft(2, '0');
    public static void Validate(Stamp stamp)
    {
        if (stamp.Renderer == Renderer ? stamp.GeometryCode is null or < 0 or > 3 : stamp.GeometryCode is not null)
            throw new InvalidDataException("幾何コードと描画方式が一致しません。");
    }
    public static float[] RedStrength(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 12_000_000 || (long)width * height * 4 != bgra.Length)
            throw new ArgumentException();
        var red = new float[width * height];
        for (int i = 0; i < red.Length; i++)
        {
            int p = i * 4;
            red[i] = Math.Max(0, bgra[p + 2] - Math.Max(bgra[p], bgra[p + 1])) / 255f * (bgra[p + 3] / 255f);
        }
        return red;
    }
    public static GeometryReading Decode(float[] red, int width, int height, ImageRegion box)
    {
        if (width <= 0 || height <= 0 || (long)width * height != red.Length || box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 ||
            (long)box.X + box.Width > width || (long)box.Y + box.Height > height) throw new ArgumentException();
        if (box.Width < 60 || box.Height < 60 || Math.Abs((double)box.Width / box.Height - 1) > 0.05)
            return new(null, null, null, "解像度不足、または円形の切り出し範囲ではありません。");
        var upper = Fit(red, width, box, 0.328);
        var lower = Fit(red, width, box, 0.672);
        if (upper is null || lower is null) return new(null, null, null, "上下の区切り線を安定して読み取れません。");
        double difference = upper.Value.Angle - lower.Value.Angle;
        double rotation = (upper.Value.Angle + lower.Value.Angle) / 2;
        int nearest = Enumerable.Range(0, 4).MinBy(code => Math.Abs(difference - Difference(code)));
        double uncertainty = 2 * Math.Sqrt(upper.Value.Error * upper.Value.Error + lower.Value.Error * lower.Value.Error);
        if (Math.Abs(rotation) > 8 || Math.Abs(difference - Difference(nearest)) + uncertainty > 0.60)
            return new(null, difference, rotation, "角度が符号の受入範囲外、または境界付近です。");
        return new(nearest, difference, rotation, "角度から読み取った2ビット候補です。真正性やイベントの一意性は証明しません。");
    }
    static (double Angle, double Error)? Fit(float[] red, int width, ImageRegion box, double fraction)
    {
        double centerX = box.X + (box.Width - 1) / 2.0;
        double expectedY = box.Y + (box.Height - 1) * fraction;
        int left = box.X + (int)(box.Width * 0.17), right = box.X + (int)(box.Width * 0.83);
        double best = -1, bestSlope = 0, bestY = expectedY;
        double offsetRange = Math.Max(1.5, box.Height * 0.025);
        for (double degrees = -10; degrees <= 10; degrees += 0.5)
        {
            double slope = Math.Tan(degrees * Math.PI / 180);
            for (double offset = -offsetRange; offset <= offsetRange; offset += 0.5)
            {
                double score = 0;
                for (int x = left; x <= right; x++)
                {
                    double y = expectedY + offset + slope * (x - centerX);
                    int iy = (int)Math.Floor(y); double f = y - iy;
                    if (iy < box.Y || iy + 1 >= box.Y + box.Height) continue;
                    score += red[iy * width + x] * (1 - f) + red[(iy + 1) * width + x] * f;
                }
                if (score > best) { best = score; bestSlope = slope; bestY = expectedY + offset; }
            }
        }
        double halfWindow = Math.Max(1.4, box.Height * 0.016);
        var points = new List<(double X, double Y)>();
        for (int x = left; x <= right; x++)
        {
            double predicted = bestY + bestSlope * (x - centerX), mass = 0, moment = 0;
            for (int y = (int)Math.Ceiling(predicted - halfWindow); y <= predicted + halfWindow; y++)
            {
                if (y < box.Y || y >= box.Y + box.Height) continue;
                double weight = red[y * width + x]; mass += weight; moment += weight * y;
            }
            if (mass >= 0.10) points.Add((x - centerX, moment / mass));
        }
        if (points.Count < (right - left + 1) * 0.85) return null;
        double meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        double xx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        double slopeFit = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / xx;
        double error = points.Sum(p => Math.Pow(p.Y - meanY - slopeFit * (p.X - meanX), 2));
        if (Math.Sqrt(error / points.Count) > Math.Max(0.45, box.Height * 0.006)) return null;
        double angle = Math.Atan(slopeFit) * 180 / Math.PI;
        double standardError = Math.Sqrt(error / (points.Count - 2) / xx) * 180 / Math.PI;
        return (angle, standardError);
    }
}
