namespace JTCStamper.Core;

public sealed record ImageRegion(int X, int Y, int Width, int Height);
public sealed record StampTemplate(Stamp Stamp, bool[] Ink, string? PngSha256 = null);
public sealed record VisualCandidate(Stamp Stamp, double Score, int Rotation, TextSimilarity Text, string? PngSha256 = null);
public sealed record TextSimilarity(double Name, double Date, double Bottom)
{
    public double Contribution => (Name + Date + Bottom) / 10;
}

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
    public static IReadOnlyList<ImageRegion> Detect(bool[] mask, int width, int height, CancellationToken cancellationToken = default)
    {
        Validate(mask, width, height);
        cancellationToken.ThrowIfCancellationRequested();
        var found = new List<ImageRegion>();
        // Search multiple scales so short gaps reconnect for discovery. Decode always uses original pixels.
        for (int scale = 1; scale <= 64 && Math.Min(width, height) / scale >= 28; scale *= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int w = (width + scale - 1) / scale, h = (height + scale - 1) / scale;
            var reduced = scale == 1 ? mask : new bool[w * h];
            if (scale > 1)
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                        if (mask[y * width + x]) reduced[(y / scale) * w + x / scale] = true;
                }
            foreach (var region in DetectSingle(reduced, w, h, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImageRegion mapped = region;
                if (scale > 1)
                {
                    int left = width, top = height, right = -1, bottom = -1;
                    int x0 = Math.Max(0, (region.X - 1) * scale), y0 = Math.Max(0, (region.Y - 1) * scale);
                    int x1 = Math.Min(width, (region.X + region.Width + 1) * scale), y1 = Math.Min(height, (region.Y + region.Height + 1) * scale);
                    for (int y = y0; y < y1; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = x0; x < x1; x++) if (mask[y * width + x])
                        { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                    }
                    if (right < left) continue;
                    mapped = new(left, top, right - left + 1, bottom - top + 1);
                }
                int duplicate = found.FindIndex(old => Overlap(old, mapped) > 0.6);
                if (duplicate < 0) found.Add(mapped);
                else
                {
                    var old = found[duplicate];
                    // A fine-scale component may omit a detached arc. Prefer a containing,
                    // more circular box from a coarser search, without expanding into nearby ink.
                    if (mapped.X <= old.X && mapped.Y <= old.Y &&
                        mapped.X + mapped.Width >= old.X + old.Width && mapped.Y + mapped.Height >= old.Y + old.Height &&
                        (double)mapped.Width * mapped.Height <= 1.5 * old.Width * old.Height &&
                        Math.Abs((double)mapped.Width / mapped.Height - 1) <= 0.04 &&
                        Math.Abs(Math.Log((double)mapped.Width / mapped.Height)) < Math.Abs(Math.Log((double)old.Width / old.Height)))
                        found[duplicate] = mapped;
                }
            }
        }
        // A closed glyph inside an already detected stamp is not a second stamp.
        return found.Where(inner =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return !found.Any(outer => outer != inner &&
            (long)outer.Width * outer.Height > 2L * inner.Width * inner.Height &&
            inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height);
        })
            .OrderBy(x => x.Y).ThenBy(x => x.X).ToArray();
    }
    static double Overlap(ImageRegion a, ImageRegion b)
    {
        double area = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) *
            (double)Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return area / ((double)a.Width * a.Height + (double)b.Width * b.Height - area);
    }
    static IReadOnlyList<ImageRegion> DetectSingle(bool[] mask, int width, int height, CancellationToken cancellationToken)
    {
        Validate(mask, width, height);
        // Join small antialiasing/JPEG gaps in a circle. Classification uses the original mask.
        var joined = new bool[mask.Length];
        for (int y = 1; y < height - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 1; x < width - 1; x++)
                if (mask[y * width + x])
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) joined[(y + dy) * width + x + dx] = true;
        }
        var queue = new Queue<int>(); var found = new List<ImageRegion>();
        for (int p = 0; p < joined.Length; p++)
        {
            if ((p & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!joined[p]) continue;
            joined[p] = false; queue.Enqueue(p);
            int left = width, right = 0, top = height, bottom = 0;
            int visited = 0;
            while (queue.TryDequeue(out int at))
            {
                if ((visited++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
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
    public static IReadOnlyList<VisualCandidate> Rank(bool[] ink, IReadOnlyList<StampTemplate> templates, int? readCode = null, CancellationToken cancellationToken = default, string? codeRenderer = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ink.Length != Side * Side || templates.Any(x => x.Ink.Length != Side * Side)) throw new ArgumentException();
        // Ignore the common outer circle; compare interior text and separators symmetrically.
        var variants = Enumerable.Range(-8, 17).Select(angle => (Angle: angle, Ink: Rotate(ink, angle))).ToArray();
        var ranked = new List<VisualCandidate>();
        foreach (var template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double best = 0; int rotation = 0; TextSimilarity text = new(0, 0, 0);
            foreach (var variant in variants)
            {
                double score = Similarity(variant.Ink, template.Ink);
                if (score > best) { best = score; rotation = variant.Angle; text = CompareText(variant.Ink, template.Ink); }
            }
            if (best >= 0.72) ranked.Add(new(template.Stamp, best, rotation, text, template.PngSha256));
        }
        return ranked.OrderByDescending(x => CorrespondenceScore.Calculate(x.Text, codeRenderer is null || x.Stamp.Renderer == codeRenderer ? readCode : null, x.Stamp.GeometryCode).Total).ThenByDescending(x => x.Score).Take(8).ToArray();
    }
    // Normalized circle bounds: exclude the two separators near y=31 and y=64.
    // Keep the three text bands independent so a large name cannot dominate the date.
    public static TextSimilarity CompareText(bool[] a, bool[] b)
    {
        if (a.Length != Side * Side || b.Length != Side * Side) throw new ArgumentException();
        return new(Similarity(a, b, 2, 29), Similarity(a, b, 35, 61), Similarity(a, b, 67, 94));
    }
    static bool Interior(int x, int y) => (x - 47.5) * (x - 47.5) + (y - 47.5) * (y - 47.5) < 40 * 40;
    static double Similarity(bool[] a, bool[] b, int fromY = 2, int toY = Side - 2)
    {
        int countA = 0, countB = 0, hitA = 0, hitB = 0;
        for (int y = fromY; y < toY; y++) for (int x = 2; x < Side - 2; x++)
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
