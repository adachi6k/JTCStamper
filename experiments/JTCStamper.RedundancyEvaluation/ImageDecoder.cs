using JTCStamper.Core;
static class ImageDecoder
{
    public static int?[] Decode(float[] red, int width, int height, ImageRegion box, string mode)
    {
        if (box.Width < 80 || box.Height < 80 || Math.Abs((double)box.Width / box.Height - 1) > 0.04) return new int?[4];
        bool split = mode != "ring32";
        int count = mode == "split40" ? 36 : split ? 28 : 32;
        var accepted = Enumerable.Range(0, 4).Select(_ => new HashSet<int>()).ToArray();
        // Search a bounded orientation range without using a known payload or history.
        foreach (double centerX in new[] { -0.5, 0.0, 0.5 })
        foreach (double centerY in new[] { -0.5, 0.0, 0.5 })
        for (double rotation = -8; rotation <= 8; rotation += 0.25)
        {
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                double value = 0;
                for (int offset = -1; offset <= 1; offset++)
                {
                    double a = (Angle(i, count) + rotation + offset * 0.3) * Math.PI / 180;
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
            if (on < 0.16 || on - off < 0.12 || off > on * 0.50) continue;
            double threshold = (on + off) / 2, margin = (on - off) * 0.12;
            // Marker stays exact, with the original confidence margin. Payload is hard-decoded.
            if (Enumerable.Range(0, 8).Any(i => Math.Abs(values[i] - threshold) < margin ||
                (values[i] < threshold) != (((0xD3 >> (7-i)) & 1) != 0))) continue;
            uint prefix = 0;
            for (int i = 8; i < count; i++) prefix = (prefix << 1) | (values[i] < threshold ? 1u : 0);
            foreach (int delta in split ? new[] { -3, -1, 1, 3 } : new[] { 0 })
            {
                uint word = prefix;
                if (split)
                {
                    for (int row = 0; row < 2; row++) foreach (int t in new[] { -24, 24 })
                    {
                        double value = 0;
                        for (int along = -1; along <= 1; along++)
                        {
                            double peak = 0;
                            for (double normal = -0.8; normal <= 0.8; normal += 0.4)
                            {
                                double lineAngle = (row == 0 ? delta : -delta) / 2.0 * Math.PI / 180;
                                double lx = (t + along * 0.4) * Math.Cos(lineAngle) - normal * Math.Sin(lineAngle);
                                double ly = (row == 0 ? -15 : 15) + (t + along * 0.4) * Math.Sin(lineAngle) + normal * Math.Cos(lineAngle);
                                double r = rotation * Math.PI / 180;
                                double x = box.X + (box.Width-1)/2.0 + centerX + (lx*Math.Cos(r)-ly*Math.Sin(r))*box.Width/87.1;
                                double y = box.Y + (box.Height-1)/2.0 + centerY + (lx*Math.Sin(r)+ly*Math.Cos(r))*box.Height/87.1;
                                peak = Math.Max(peak, Sample(red, width, height, x, y));
                            }
                            value += peak;
                        }
                        word = (word << 1) | (value/3 < threshold ? 1u : 0);
                    }
                }
                var decoded = Golay.Decode(mode == "split40" ? word >> 8 : word);
                if (mode == "split40" && decoded is not null && (word & 255) != Checksum(decoded.Value.Code)) continue;
                if (decoded is null || (split && (decoded.Value.Code & 3)*2-3 != delta)) continue;
                for (int limit = decoded.Value.Corrections; limit <= 3; limit++) accepted[limit].Add(decoded.Value.Code);
            }
        }
        // Reject conflicting payloads across every sampled alignment; never consult history/expected code.
        return accepted.Select(set => set.Count == 1 ? (int?)set.Single() : null).ToArray();
    }
    static byte Checksum(int code)
    {
        byte crc = 0;
        foreach (byte value in new byte[] { 0xD3, (byte)(code >> 8), (byte)code })
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (byte)((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0));
        }
        return crc;
    }
    static double Angle(int index, int count) => (index < count/2 ? 37 : 217) + (index % (count/2))*106.0/(count/2-1);

    static double Sample(float[] red, int width, int height, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix + 1 >= width || iy + 1 >= height) return 0;
        double fx = x - ix, fy = y - iy;
        return red[iy * width + ix] * (1 - fx) * (1 - fy) + red[iy * width + ix + 1] * fx * (1 - fy)
            + red[(iy + 1) * width + ix] * (1 - fx) * fy + red[(iy + 1) * width + ix + 1] * fx * fy;
    }
}
