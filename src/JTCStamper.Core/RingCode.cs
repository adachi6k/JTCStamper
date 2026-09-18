using System.Security.Cryptography;

namespace JTCStamper.Core;

// 12 payload bits + 8-bit format/orientation marker + CRC-8. None is authentication.
public static class RingCode
{
    public const string Renderer = "wpf-v4-ring12";
    public const int CellCount = 28;
    const byte Sync = 0xD3;
    public static int ForEvent(Guid id)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        return (hash[0] | hash[1] << 8) & 0xFFF;
    }
    public static double CellAngle(int index)
    {
        if (index < 0 || index >= CellCount) throw new ArgumentOutOfRangeException(nameof(index));
        return (index < 14 ? 37 : 217) + (index % 14) * (106.0 / 13);
    }
    public static bool[] Encode(int code)
    {
        if (code < 0 || code > 4095) throw new ArgumentOutOfRangeException(nameof(code));
        uint word = ((uint)Sync << 20) | ((uint)code << 8) | Checksum(code);
        return Enumerable.Range(0, CellCount).Select(i => ((word >> (27 - i)) & 1) != 0).ToArray();
    }
    public static int? ReadCells(IReadOnlyList<bool> cells)
    {
        if (cells.Count != CellCount) throw new ArgumentException();
        uint word = 0;
        foreach (bool bit in cells) word = (word << 1) | (bit ? 1u : 0);
        int code = (int)((word >> 8) & 0xFFF);
        return (word >> 20) == Sync && (word & 255) == Checksum(code) ? code : null;
    }
    static byte Checksum(int code)
    {
        // CRC-8 polynomial 0x07, init 0; format marker and big-endian 12-bit value.
        byte crc = 0;
        foreach (byte value in new[] { Sync, (byte)(code >> 8), (byte)code })
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }
        return crc;
    }
    public static GeometryReading Decode(float[] red, int width, int height, ImageRegion box, GeometryReading angleReading)
    {
        if (width <= 0 || height <= 0 || (long)width * height != red.Length || box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 ||
            (long)box.X + box.Width > width || (long)box.Y + box.Height > height) throw new ArgumentException();
        if (box.Width < 80 || box.Height < 80 || Math.Abs((double)box.Width / box.Height - 1) > 0.04)
            return new(null, null, null, "12ビットの読み取りには直径80px以上の円形領域が必要です。", 12);
        var accepted = new Dictionary<int, double>();
        // Search a bounded orientation range without using a known payload or history.
        for (double rotation = -8; rotation <= 8; rotation += 0.25)
        {
            var values = new double[CellCount];
            for (int i = 0; i < CellCount; i++)
            {
                double value = 0;
                for (double radius = 39; radius <= 41; radius += 0.5)
                    for (int offset = -1; offset <= 1; offset++)
                    {
                        double a = (CellAngle(i) + rotation + offset * 0.4) * Math.PI / 180;
                        double x = box.X + (box.Width - 1) / 2.0 + radius * Math.Cos(a) * box.Width / 87.1;
                        double y = box.Y + (box.Height - 1) / 2.0 + radius * Math.Sin(a) * box.Height / 87.1;
                        value += Sample(red, width, height, x, y);
                    }
                values[i] = value / 15;
            }
            // The known marker must contain both strong and weak cells; reject faint/noisy marks.
            double on = 0, off = 0; int onCount = 0, offCount = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((Sync >> (7 - i)) & 1) != 0) { on += values[i]; onCount++; }
                else { off += values[i]; offCount++; }
            }
            on /= onCount; off /= offCount;
            if (on < 0.16 || on - off < 0.14 || off > on * 0.35) continue;
            double threshold = (on + off) / 2, margin = (on - off) * 0.16;
            if (values.Any(v => Math.Abs(v - threshold) < margin)) continue;
            int? code = ReadCells(values.Select(v => v >= threshold).ToArray());
            if (code is null || (angleReading.Code is int low && (code.Value & 3) != low)) continue;
            if (!accepted.ContainsKey(code.Value)) accepted.Add(code.Value, rotation);
        }
        if (accepted.Count != 1) return new(null, angleReading.DifferenceDegrees, null,
            accepted.Count == 0 ? "目盛り・形式マーカー・CRCを確認できませんでした。" : "複数のコードが読めたため確定しません。", 12);
        var match = accepted.Single();
        return new(match.Key, angleReading.DifferenceDegrees, match.Value, "12ビット候補です。CRCは誤読検査であり認証ではありません。", 12);
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
