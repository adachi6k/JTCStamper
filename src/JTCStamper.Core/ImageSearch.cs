namespace JTCStamper.Core;

public sealed record ImageRegion(int X, int Y, int Width, int Height);
public sealed record StampTemplate(Stamp Stamp, bool[] Ink);
public sealed record VisualCandidate(Stamp Stamp, double Score, int Rotation);

// Candidate retrieval only: no visual score is authentication or an event identifier.
public static class ImageSearch
{
    public const int Side = 96;
    public static bool[] RedMask(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 12_000_000 || bgra.Length != (long)width * height * 4)
            throw new ArgumentException("画像は1200万画素以内にしてください。");
        var mask = new bool[width * height];
        for (int i = 0; i < mask.Length; i++)
        {
            int p = i * 4, b = bgra[p], g = bgra[p + 1], red = bgra[p + 2], alpha = bgra[p + 3];
            mask[i] = alpha >= 40 && red > g + 25 && red > b + 20 && red > 65;
        }
        return mask;
    }
    public static IReadOnlyList<ImageRegion> Detect(bool[] mask, int width, int height)
    {
        Validate(mask, width, height);
        // Join small antialiasing/JPEG gaps in a circle. Classification uses the original mask.
        var joined = new bool[mask.Length];
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
                if (mask[y * width + x])
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) joined[(y + dy) * width + x + dx] = true;
        var queue = new Queue<int>(); var found = new List<ImageRegion>();
        for (int p = 0; p < joined.Length; p++)
        {
            if (!joined[p]) continue;
            joined[p] = false; queue.Enqueue(p);
            int left = width, right = 0, top = height, bottom = 0;
            while (queue.TryDequeue(out int at))
            {
                int x = at % width, y = at / width;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (joined[next]) { joined[next] = false; queue.Enqueue(next); }
                }
            }
            int w = right - left - 1, h = bottom - top - 1;
            if (w < 28 || h < 28 || w > h * 1.2 || h > w * 1.2) continue;
            var region = new ImageRegion(left + 1, top + 1, w, h);
            var ink = Normalize(mask, width, height, region);
            int circle = 0;
            for (int angle = 0; angle < 72; angle++)
            {
                double radians = angle * Math.PI / 36;
                bool hit = false;
                for (int radius = 43; radius <= 48; radius++)
                {
                    int x = (int)Math.Round(47.5 + radius * Math.Cos(radians));
                    int y = (int)Math.Round(47.5 + radius * Math.Sin(radians));
                    if (x >= 0 && x < Side && y >= 0 && y < Side && ink[y * Side + x]) hit = true;
                }
                if (hit) circle++;
            }
            if (circle >= 54) found.Add(region);
        }
        return found.OrderBy(x => x.Y).ThenBy(x => x.X).ToArray();
    }
    public static ImageRegion? Bounds(bool[] mask, int width, int height)
    {
        Validate(mask, width, height);
        int left = width, top = height, right = -1, bottom = -1;
        for (int p = 0; p < mask.Length; p++) if (mask[p])
        { int x = p % width, y = p / width; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right < 0 ? null : new(left, top, right - left + 1, bottom - top + 1);
    }
    public static bool[] Normalize(bool[] mask, int width, int height, ImageRegion region)
    {
        Validate(mask, width, height);
        if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0 ||
            (long)region.X + region.Width > width || (long)region.Y + region.Height > height) throw new ArgumentException("切り出し範囲が不正です。");
        var output = new bool[Side * Side];
        // Area occupancy preserves thin strokes when reducing large source images.
        for (int y = 0; y < Side; y++) for (int x = 0; x < Side; x++)
        {
            int x0 = region.X + x * region.Width / Side, y0 = region.Y + y * region.Height / Side;
            int x1 = region.X + Math.Max(x * region.Width / Side + 1, (x + 1) * region.Width / Side);
            int y1 = region.Y + Math.Max(y * region.Height / Side + 1, (y + 1) * region.Height / Side);
            for (int sy = y0; sy < y1; sy++) for (int sx = x0; sx < x1; sx++)
                if (mask[sy * width + sx]) output[y * Side + x] = true;
        }
        return output;
    }
    public static IReadOnlyList<VisualCandidate> Rank(bool[] ink, IReadOnlyList<StampTemplate> templates)
    {
        if (ink.Length != Side * Side || templates.Any(x => x.Ink.Length != Side * Side)) throw new ArgumentException();
        // Ignore the common outer circle; compare interior text and separators symmetrically.
        var variants = Enumerable.Range(-8, 17).Select(angle => (Angle: angle, Ink: Rotate(ink, angle))).ToArray();
        var ranked = new List<VisualCandidate>();
        foreach (var template in templates)
        {
            double best = 0; int rotation = 0;
            foreach (var variant in variants)
            {
                double score = Similarity(variant.Ink, template.Ink);
                if (score > best) { best = score; rotation = variant.Angle; }
            }
            if (best >= 0.72) ranked.Add(new(template.Stamp, best, rotation));
        }
        return ranked.OrderByDescending(x => x.Score).Take(8).ToArray();
    }
    static bool Interior(int x, int y) => (x - 47.5) * (x - 47.5) + (y - 47.5) * (y - 47.5) < 40 * 40;
    static double Similarity(bool[] a, bool[] b)
    {
        int countA = 0, countB = 0, hitA = 0, hitB = 0;
        for (int y = 2; y < Side - 2; y++) for (int x = 2; x < Side - 2; x++)
        {
            if (!Interior(x, y)) continue;
            int p = y * Side + x;
            if (a[p]) { countA++; if (Near(b, x, y)) hitA++; }
            if (b[p]) { countB++; if (Near(a, x, y)) hitB++; }
        }
        if (countA < 30 || countB < 30) return 0;
        double pa = (double)hitA / countA, pb = (double)hitB / countB;
        return pa + pb == 0 ? 0 : 2 * pa * pb / (pa + pb);
    }
    static bool Near(bool[] ink, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (ink[(y + dy) * Side + x + dx]) return true;
        return false;
    }
    static bool[] Rotate(bool[] ink, int degrees)
    {
        var output = new bool[Side * Side]; double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        for (int y = 0; y < Side; y++) for (int x = 0; x < Side; x++)
        {
            int sx = (int)Math.Round(c * (x - 47.5) - s * (y - 47.5) + 47.5);
            int sy = (int)Math.Round(s * (x - 47.5) + c * (y - 47.5) + 47.5);
            if (sx >= 0 && sx < Side && sy >= 0 && sy < Side) output[y * Side + x] = ink[sy * Side + sx];
        }
        return output;
    }
    static void Validate(bool[] mask, int width, int height)
    { if (width <= 0 || height <= 0 || (long)width * height != mask.Length) throw new ArgumentException(); }
}
