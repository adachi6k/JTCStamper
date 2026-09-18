using JTCStamper.Core;

// Experiment only. 8-bit format marker + payload + CRC-8; none authenticates an image.
static class ColorLineCodec
{
    public static int PayloadBits(string mode) => mode switch { "ring12" or "split12" => 12, "split16" or "hybrid16" => 16, _ => throw new ArgumentException(nameof(mode)) };
    public static int RingPayloadBits(string mode) => PayloadBits(mode) - (mode == "ring12" ? 0 : 4);
    public static int Tag(string mode) => mode switch { "ring12" => 0xD3, "split12" => 0xD4, "split16" => 0xD5, "hybrid16" => 0xD6, _ => throw new ArgumentException(nameof(mode)) };
    public static double Angle(int index, int count) => (index < count / 2 ? 37 : 217) + (index % (count / 2)) * 106.0 / (count / 2 - 1);
    static int Crc(int code, string mode)
    {
        int crc = 0;
        foreach (int value in new[] { Tag(mode), code >> 8, code & 255 })
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = ((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0)) & 255;
        }
        return crc;
    }
    public static bool[] Encode(int code, string mode)
    {
        if (code < 0 || code >= (1 << PayloadBits(mode))) throw new ArgumentOutOfRangeException(nameof(code));
        int rp = RingPayloadBits(mode), count = rp + 16;
        uint word = (uint)((Tag(mode) << (rp + 8)) | ((code & ((1 << rp) - 1)) << 8) | Crc(code, mode));
        var result = new bool[PayloadBits(mode) + 16];
        for (int i = 0; i < count; i++) result[i] = ((word >> (count - 1 - i)) & 1) != 0;
        for (int i = count; i < result.Length; i++) result[i] = ((code >> (PayloadBits(mode) - 1 - (i - count))) & 1) != 0;
        return result;
    }
    public static int? Read(bool[] cells, string mode)
    {
        if (cells.Length != PayloadBits(mode) + 16) throw new ArgumentException(nameof(cells));
        int rp = RingPayloadBits(mode), count = rp + 16; uint word = 0;
        for (int i = 0; i < count; i++) word = (word << 1) | (cells[i] ? 1u : 0);
        int high = 0;
        for (int i = count; i < cells.Length; i++) high = (high << 1) | (cells[i] ? 1 : 0);
        int code = (high << rp) | ((int)(word >> 8) & ((1 << rp) - 1));
        return (word >> (rp + 8)) == Tag(mode) && (word & 255) == Crc(code, mode) ? code : null;
    }
    public static RingReading Decode(byte[] bgra, int width, int height, ImageRegion box, string mode)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length || box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 || box.X + box.Width > width || box.Y + box.Height > height) throw new ArgumentException();
        if (box.Width < 80 || box.Height < 80 || Math.Abs((double)box.Width / box.Height - 1) > 0.04) return new(null, null, "Invalid circular bounds or smaller than 80px");
        var strength = new float[width * height]; var chroma = new float[strength.Length];
        for (int p = 0; p < strength.Length; p++)
        {
            int i = p * 4; float a = bgra[i + 3] / 255f;
            strength[p] = (bgra[i + 2] - (bgra[i + 1] + bgra[i]) / 2f) * a / 255f;
            chroma[p] = (bgra[i + 1] - bgra[i]) * a / 255f;
        }
        int rp = RingPayloadBits(mode), ringCount = rp + 16, tag = Tag(mode);
        var accepted = new Dictionary<int, double>();
        foreach (double cx in new[] { -.5, 0, .5 }) foreach (double cy in new[] { -.5, 0, .5 })
        for (double rotation = -8; rotation <= 8; rotation += .25)
        {
            var values = new double[PayloadBits(mode) + 16]; bool valid = true;
            for (int i = 0; i < ringCount; i++)
            {
                double value = 0;
                for (int da = -1; da <= 1; da++)
                {
                    double a = (Angle(i, ringCount) + rotation + da * .3) * Math.PI / 180;
                    double peak = 0, hue = 0;
                    for (double radius = 42.2; radius <= 43.8; radius += .2)
                    {
                        double x = box.X + (box.Width - 1) / 2.0 + cx + radius * Math.Cos(a) * box.Width / 87.1;
                        double y = box.Y + (box.Height - 1) / 2.0 + cy + radius * Math.Sin(a) * box.Height / 87.1;
                        double s = Sample(strength, width, height, x, y);
                        if (s > peak) { peak = s; hue = Sample(chroma, width, height, x, y) / s; }
                    }
                    if (mode != "hybrid16" && peak < .10) { valid = false; break; }
                    value += mode == "hybrid16" ? peak : hue;
                }
                if (!valid) break;
                values[i] = value / 3;
            }
            if (!valid) continue;
            double zero = 0, one = 0; int nz = 0, no = 0;
            for (int i = 0; i < 8; i++) if (((tag >> (7 - i)) & 1) != 0) { one += values[i]; no++; } else { zero += values[i]; nz++; }
            zero /= nz; one /= no;
            double difference = one - zero;
            if (mode == "hybrid16" ? (zero < .16 || zero - one < .12 || one > zero * .50) : Math.Abs(difference) < .10) continue;
            bool Confident(double value) { double t = (value - zero) / difference; return t < .38 || t > .62; }
            bool Bit(double value) => (value - zero) / difference > .5;
            if (values.Take(ringCount).Any(v => !Confident(v))) continue;
            var cells = new bool[values.Length];
            for (int i = 0; i < ringCount; i++) cells[i] = Bit(values[i]);
            // Low payload bits determine the existing line slope; this does not add independent bits.
            int low = 0; for (int i = 8; i < 8 + rp; i++) low = (low << 1) | (cells[i] ? 1 : 0);
            double delta = (low & 3) * 2 - 3, rot = rotation * Math.PI / 180;
            for (int i = ringCount; i < values.Length; i++)
            {
                int cell = i - ringCount, row = cell / 2;
                double y0 = row == 0 ? -15 : 15, t0 = cell % 2 == 0 ? -24 : 24;
                double slope = (row == 0 ? delta / 2 : -delta / 2) * Math.PI / 180;
                double value = 0;
                for (int along = -1; along <= 1; along++)
                {
                    double peak = 0, hue = 0;
                    for (double normal = -1; normal <= 1; normal += .25)
                    {
                        double lx = (t0 + along) * Math.Cos(slope) - normal * Math.Sin(slope);
                        double ly = y0 + (t0 + along) * Math.Sin(slope) + normal * Math.Cos(slope);
                        double x = box.X + (box.Width - 1) / 2.0 + cx + (lx * Math.Cos(rot) - ly * Math.Sin(rot)) * box.Width / 87.1;
                        double y = box.Y + (box.Height - 1) / 2.0 + cy + (lx * Math.Sin(rot) + ly * Math.Cos(rot)) * box.Height / 87.1;
                        double s = Sample(strength, width, height, x, y);
                        if (s > peak) { peak = s; hue = Sample(chroma, width, height, x, y) / s; }
                    }
                    if (peak < .10) { valid = false; break; }
                    value += hue;
                }
                value /= 3;
                if (mode == "hybrid16")
                {
                    double reference = 0;
                    foreach (double along in new[] { -5.0, 5.0 })
                    {
                        double peak = 0, hue = 0;
                        for (double normal = -1; normal <= 1; normal += .25)
                        {
                            double lx = (t0 + along) * Math.Cos(slope) - normal * Math.Sin(slope);
                            double ly = y0 + (t0 + along) * Math.Sin(slope) + normal * Math.Cos(slope);
                            double x = box.X + (box.Width - 1) / 2.0 + cx + (lx * Math.Cos(rot) - ly * Math.Sin(rot)) * box.Width / 87.1;
                            double y = box.Y + (box.Height - 1) / 2.0 + cy + (lx * Math.Sin(rot) + ly * Math.Cos(rot)) * box.Height / 87.1;
                            double s = Sample(strength, width, height, x, y);
                            if (s > peak) { peak = s; hue = Sample(chroma, width, height, x, y) / s; }
                        }
                        if (peak < .10) { valid = false; break; }
                        reference += hue;
                    }
                    double shift = value - reference / 2;
                    if (!valid || (shift > .06 && shift < .15)) { valid = false; break; }
                    cells[i] = shift >= .15;
                }
                else
                {
                    if (!valid || !Confident(value)) { valid = false; break; }
                    cells[i] = Bit(value);
                }
            }
            if (!valid) continue;
            int? code = Read(cells, mode);
            if (code.HasValue) accepted.TryAdd(code.Value, rotation);
        }
        if (accepted.Count != 1) return new(null, null, accepted.Count == 0 ? "No color/marker/CRC match" : "Multiple codes; rejected");
        var match = accepted.Single(); return new(match.Key, match.Value, "Experimental candidate only; CRC is not authentication");
    }
    static double Sample(float[] data, int width, int height, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix + 1 >= width || iy + 1 >= height) return 0;
        double fx = x - ix, fy = y - iy;
        return data[iy * width + ix] * (1 - fx) * (1 - fy) + data[iy * width + ix + 1] * fx * (1 - fy)
            + data[(iy + 1) * width + ix] * (1 - fx) * fy + data[(iy + 1) * width + ix + 1] * fx * fy;
    }
}
