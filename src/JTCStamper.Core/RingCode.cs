using System.Security.Cryptography;

namespace JTCStamper.Core;

public sealed record RingReading(int? Code, double? RotationDegrees, string Reason);

// 12 payload bits + 8-bit format/orientation marker + CRC-8. None is authentication.
public static class RingCode
{
    public const string PlainRenderer = "wpf-plain-v1";
    public const string Renderer = "wpf-v5-gap12";
    public const int CellCount = 28;
    public const double GapDegrees = 3.5;
    public static string Label(int code) => Convert.ToString(code, 2).PadLeft(12, '0');
    public static double SeparatorDifference(int code) => (code & 3) * 2 - 3;
    public static void Validate(Stamp stamp)
    {
        if (stamp.Renderer == PlainRenderer)
        {
            if (stamp.GeometryCode is not null) throw new InvalidDataException("プレーン印影に幾何コードは指定できません。");
            return;
        }
        if (stamp.Renderer != Renderer) throw new NotSupportedException("対応していない印影形式です。現在の12ビット形式とプレーン形式を使用できます。");
        if (stamp.GeometryCode is null or < 0 or > 4095) throw new InvalidDataException("12ビットの幾何コードが不正です。");
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
    public static RingReading Decode(float[] red, int width, int height, ImageRegion box, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            var values = new double[CellCount];
            for (int i = 0; i < CellCount; i++)
            {
                double value = 0;
                for (int offset = -1; offset <= 1; offset++)
                {
                    double a = (CellAngle(i) + rotation + offset * 0.3) * Math.PI / 180;
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
                if (((Sync >> (7 - i)) & 1) == 0) { on += values[i]; onCount++; }
                else { off += values[i]; offCount++; }
            }
            on /= onCount; off /= offCount;
            if (on < 0.16 || on - off < 0.12 || off > on * 0.50) continue;
            double threshold = (on + off) / 2, margin = (on - off) * 0.12;
            if (values.Any(v => Math.Abs(v - threshold) < margin)) continue;
            int? code = ReadCells(values.Select(v => v < threshold).ToArray());
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
