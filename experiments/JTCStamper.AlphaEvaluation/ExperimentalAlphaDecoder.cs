// Experimental only: gap decoder ad601a0 with adjustable marker contrast thresholds (evaluation only).
// No opacity, expected code, or history is supplied to the decoder.
using JTCStamper.Core;
static class ExperimentalAlphaDecoder
{
    public static RingReading Decode(float[] red, int width, int height, ImageRegion box, double minimumContrast = 0.08, double maximumLowHigh = 0.85)
    {
        if (width <= 0 || height <= 0 || (long)width * height != red.Length || box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 ||
            (long)box.X + box.Width > width || (long)box.Y + box.Height > height) throw new ArgumentException();
        if (box.Width < 80 || box.Height < 80 || Math.Abs((double)box.Width / box.Height - 1) > 0.04)
            return new(null, null, "12ビットの読み取りには直径80px以上の円形領域が必要です。");
        var accepted = new Dictionary<int, double>();
        // Search a bounded orientation range without using a known payload or history.
        foreach (double centerX in new[] { -0.5, 0.0, 0.5 })
        foreach (double centerY in new[] { -0.5, 0.0, 0.5 })
        for (double rotation = -8; rotation <= 8; rotation += 0.25)
        {
            var values = new double[RingCode.CellCount];
            for (int i = 0; i < RingCode.CellCount; i++)
            {
                double value = 0;
                for (int offset = -1; offset <= 1; offset++)
                {
                    double a = (RingCode.CellAngle(i) + rotation + offset * 0.3) * Math.PI / 180;
                    double peak = 0;
                    for (double radius = 42.2; radius <= 43.8; radius += 0.2)
                    {
                        double x = box.X + (box.Width - 1) / 2.0 + centerX + radius * Math.Cos(a) * box.Width / 87.1;
                        double y = box.Y + (box.Height - 1) / 2.0 + centerY + radius * Math.Sin(a) * box.Height / 87.1;
                        peak = Math.Max(peak, Sample(red, width, height, x, y));
                    }
                    value += peak;
                }
                values[i] = value / 3;
            }
            // The known marker must contain both strong and weak cells; reject faint/noisy marks.
            double on = 0, off = 0; int onCount = 0, offCount = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((0xD3 >> (7 - i)) & 1) == 0) { on += values[i]; onCount++; }
                else { off += values[i]; offCount++; }
            }
            on /= onCount; off /= offCount;
            if (on < 0.16 || on - off < minimumContrast || off > on * maximumLowHigh) continue;
            double threshold = (on + off) / 2, margin = (on - off) * 0.12;
            if (values.Any(v => Math.Abs(v - threshold) < margin)) continue;
            int? code = RingCode.ReadCells(values.Select(v => v < threshold).ToArray());
            if (code is null) continue;
            if (!accepted.ContainsKey(code.Value)) accepted.Add(code.Value, rotation);
        }
        if (accepted.Count != 1) return new(null, null,
            accepted.Count == 0 ? "円周の欠け・形式マーカー・CRCを確認できませんでした。" : "複数のコードが読めたため確定しません。");
        var match = accepted.Single();
        return new(match.Key, match.Value, "12ビット候補です。CRCは誤読検査であり認証ではありません。");
    }
    static double Sample(float[] red, int width, int height, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix + 1 >= width || iy + 1 >= height) return 0;
        double fx = x - ix, fy = y - iy;
        return red[iy * width + ix] * (1 - fx) * (1 - fy) + red[iy * width + ix + 1] * fx * (1 - fy)
            + red[(iy + 1) * width + ix] * (1 - fx) * fy + red[(iy + 1) * width + ix + 1] * fx * fy;
    }
}
