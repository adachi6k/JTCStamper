using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JTCStamper.App;
using JTCStamper.Core;

internal static class Program
{
    const string Seed = "JTC-heldout-20260926-v1";
    const string FrozenAlgorithms = "40dfe09f82123c4485b0269c86a796beee4a5549";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string root = "";
    static string officeStatus = "not-run";
    static string? officeVersion;
    static readonly List<Input> inputs = [];
    static readonly List<Row> rows = [];
    sealed record Known(Generation Generation, byte[] Png, BitmapSource Image);
    sealed record Input(string Group, string Condition, int Origin, Guid? ExpectedId, int? ExpectedCode,
        string File, string Sha256, ImageRegion ExpectedBounds);
    sealed record Observation(Input Input, int Regions, int SelectedIndex, bool Detected, int? Code,
        string Reason, bool[]? Ink);
    sealed record Row(int HistoryCount, string Group, string Condition, int Origin, bool Detected,
        int Regions, int SelectedIndex, int? Code, bool CorrectCode, bool WrongCode, bool UnreadCode,
        string ExactStatus, int ExactCount, bool ExactContainsExpected, bool CandidatesContainExpected,
        string TopCategory, bool TopSetContainsExpected, int TopCount, double? TopScore, bool CandidateGreen,
        bool DisplayedGreen, string DisplayKind, int ReferenceCount, bool ReferenceLimited, string InputSha256);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var parent = Path.GetFullPath(args[0]);
        if (!File.Exists(Path.Combine(parent, ".evaluation-root"))) return 2;
        root = Path.Combine(parent, "heldout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { Run(); return 0; }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root, "failure.txt"), ex.ToString()); Console.WriteLine(ex); return 1; }
    }
    static Guid Id(string label) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Seed + "/" + label)).AsSpan(0, 16));
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    static Known Make(Guid id, Stamp stamp, int index)
    {
        stamp = stamp with { GeometryCode = stamp.Renderer == RingCode.PlainRenderer ? null : RingCode.ForEvent(id) };
        var image = StampRenderer.Render(stamp); var png = StampRenderer.Png(image);
        return new(new(id, DateTimeOffset.UnixEpoch.AddSeconds(index), stamp, Hash(png), Convert.ToBase64String(png)), png, image);
    }
    static void Run()
    {
        Console.WriteLine("Generating new held-out origins (no threshold fitting)...");
        string[] names = ["佐藤", "鈴木", "田中", "高橋", "伊藤", "渡辺", "山本", "中村", "試験A", "試験B", "TEST01", "TEST02", "検証", "確認用", "ABC", "XYZ"];
        string[] bottoms = ["確認", "承認", "受付", "検査"];
        var origins = Enumerable.Range(0, 16).Select(i => Make(Id("origin/" + i),
            new Stamp(names[i], new DateOnly(2027, 2, 3).AddDays(i * 13), bottoms[i % 4]), i)).ToArray();
        var related = origins.Select((source, i) =>
        {
            var stamp = source.Generation.Stamp;
            if (i % 4 == 0) stamp = stamp with { DisplayDate = stamp.DisplayDate.AddDays(1) };
            if (i % 4 == 3) stamp = stamp with { Bottom = "再確認" };
            Guid id = Id("related/" + i);
            for (int n = 0; (RingCode.ForEvent(id) == source.Generation.Stamp.GeometryCode) != (i % 4 == 2); n++)
            {
                if (n > 1_000_000) throw new InvalidDataException("Collision fixture search exhausted.");
                id = Id($"related/{i}/{n}");
            }
            return Make(id, stamp, 100 + i);
        }).ToArray();
        foreach (var (source, i) in origins.Select((s, i) => (s, i)))
        {
            Add("genuine", "original384", i, source, source.Png, new(18, 18, 348, 348));
            foreach (int size in new[] { 96, 72, 48 }) AddPage("genuine", "page" + size, i, source, source.Image, size);
            AddPage("genuine", "jpeg85", i, source, source.Image, 96, jpeg: true);
            AddPage("genuine", "rotation3", i, source, source.Image, 96, rotation: 3);
            AddPage("genuine", "gray-background", i, source, source.Image, 96, gray: true);
            AddPage("genuine", "horizontal110", i, source, source.Image, 96, stretch: 1.1);
            AddPage("imitation", "horizontal-lines", i, source, Imitation(source.Generation.Stamp, false), 96);
            AddPage("imitation", "copied-geometric-code", i, source, Imitation(source.Generation.Stamp, true), 96);
            Add("unauthorized-copy", "original-bytes", i, source, source.Png, new(18, 18, 348, 348));
            AddPage("unauthorized-copy", "resaved-page", i, source, source.Image, 96);
        }
        Console.WriteLine("Rendering new synthetic documents through installed PowerPoint...");
        Office(origins);
        Console.WriteLine("Preparing 1,000-record history, with target origins among the latest images...");
        var distractors = Enumerable.Range(0, 968).Select(i => Make(Id("distractor/" + i),
            new Stamp("候補" + i.ToString("000"), new DateOnly(2050, 1, 1).AddDays(i), "TEST", RingCode.PlainRenderer), 1000 + i)).ToArray();
        Add("old-reference", "original384", 0, distractors[0], distractors[0].Png, new(18, 18, 348, 348));
        AddPage("old-reference", "outside-latest300", 0, distractors[0], distractors[0].Image, 96);
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
        {
            Seed, Inputs = inputs, Origins = origins.Select(x => new { x.Generation.EventId, x.Generation.Stamp, x.Generation.PngSha256 }),
            Related = related.Select(x => new { x.Generation.EventId, x.Generation.Stamp, x.Generation.PngSha256 })
        }, Json));
        Console.WriteLine("Measuring detection/decoding once per input (including unreadable inputs)...");
        var observations = inputs.Select(Observe).ToArray();
        File.WriteAllText(Path.Combine(root, "observations.json"), JsonSerializer.Serialize(observations.Select(o => new
        {
            o.Input, o.Regions, o.SelectedIndex, o.Detected, o.Code, o.Reason
        }), Json));
        foreach (int count in new[] { 0, 32, 1000 })
        {
            Console.WriteLine($"Measuring candidates and exact matches against {count} records...");
            var all = count == 0 ? Array.Empty<Known>() : count == 32 ? origins.Concat(related).ToArray() : distractors.Concat(origins).Concat(related).ToArray();
            EvaluateHistory(count, all, observations);
            Save();
        }
        Save(); Console.WriteLine(Path.Combine(root, "evaluation.json"));
    }
    static void Add(string group, string condition, int origin, Known source, byte[] bytes, ImageRegion bounds)
    {
        string extension = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? ".jpg" : ".png";
        string file = $"{inputs.Count:D4}-{group}-{condition}" + extension;
        File.WriteAllBytes(Path.Combine(root, file), bytes);
        inputs.Add(new(group, condition, origin, group == "imitation" ? null : source.Generation.EventId,
            source.Generation.Stamp.GeometryCode, file, Hash(bytes), bounds));
    }
    static void AddPage(string group, string condition, int index, Known source, BitmapSource image, int size,
        bool jpeg = false, double rotation = 0, bool gray = false, double stretch = 1)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(gray ? Brushes.Gainsboro : Brushes.White, null, new Rect(0, 0, 640, 480));
            dc.PushTransform(new RotateTransform(rotation, 120 + size * stretch / 2, 90 + size / 2));
            dc.DrawImage(image, new Rect(120, 90, size * stretch, size)); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        byte[] bytes;
        if (jpeg)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); bytes = stream.ToArray();
        }
        else bytes = StampRenderer.Png(bitmap);
        var box = new ImageRegion((int)Math.Floor(120 + size * stretch * 4.5 / 96), (int)Math.Floor(90 + size * 4.5 / 96),
            (int)Math.Ceiling(size * stretch * 87 / 96), (int)Math.Ceiling(size * 87.0 / 96));
        Add(group, condition, index, source, bytes, box);
    }
    // Independent reconstruction from the public format, not copied product glyph/image pixels.
    static BitmapSource Imitation(Stamp stamp, bool code)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(Color.FromRgb(193, 34, 39)); var pen = new Pen(brush, 1.15);
            if (!code) dc.DrawEllipse(null, pen, new Point(48, 48), 43, 43);
            else
            {
                int value = stamp.GeometryCode!.Value; byte crc = 0;
                foreach (byte item in new[] { (byte)0xD3, (byte)(value >> 8), (byte)value })
                {
                    crc ^= item;
                    for (int bit = 0; bit < 8; bit++) crc = (byte)((crc & 128) != 0 ? (crc << 1) ^ 7 : crc << 1);
                }
                uint word = (0xD3u << 20) | ((uint)value << 8) | crc;
                // Short independent line segments approximate the encoded outer circle.
                for (double angle = 0; angle < 360; angle += .2)
                {
                    bool gap = false;
                    for (int cell = 0; cell < 28; cell++)
                    {
                        double center = (cell < 14 ? 37 : 217) + (cell % 14) * (106.0 / 13);
                        if (((word >> (27 - cell)) & 1) != 0 && Math.Abs(angle + .1 - center) < 1.75) { gap = true; break; }
                    }
                    if (gap) continue;
                    Point At(double a) => new(48 + 43 * Math.Cos(a * Math.PI / 180), 48 + 43 * Math.Sin(a * Math.PI / 180));
                    dc.DrawLine(pen, At(angle), At(angle + .2));
                }
            }
            double angleDifference = code ? (stamp.GeometryCode!.Value % 4) * 2 - 3 : 0;
            foreach (int y in new[] { 33, 63 })
            {
                double rise = Math.Tan((y == 33 ? angleDifference : -angleDifference) / 2 * Math.PI / 180) * 40;
                dc.DrawLine(pen, new Point(8, y - rise), new Point(88, y + rise));
            }
            void Text(string value, Rect area)
            {
                var text = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
                    new Typeface("Yu Gothic"), 72, brush, 1);
                var shape = text.BuildGeometry(new Point()); var b = shape.Bounds;
                if (b.IsEmpty) return;
                double scale = Math.Min(area.Width / b.Width, area.Height / b.Height);
                shape.Transform = new MatrixTransform(scale, 0, 0, scale,
                    area.X + area.Width / 2 - (b.X + b.Width / 2) * scale,
                    area.Y + area.Height / 2 - (b.Y + b.Height / 2) * scale);
                dc.DrawGeometry(brush, null, shape);
            }
            Text(stamp.Name, new Rect(18, 10, 60, 20));
            Text("'" + stamp.DisplayDate.ToString("yy.MM.dd", CultureInfo.InvariantCulture), new Rect(10, 36, 76, 24));
            Text(stamp.Bottom, new Rect(18, 66, 60, 20));
        }
        var result = new RenderTargetBitmap(384, 384, 384, 384, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    static void Office(Known[] sources)
    {
        bool existed = Process.GetProcessesByName("POWERPNT").Length != 0;
        dynamic? app = null, document = null;
        try
        {
            var type = Type.GetTypeFromProgID("PowerPoint.Application") ?? throw new NotSupportedException("PowerPoint unavailable.");
            app = Activator.CreateInstance(type)!; officeVersion = app.Version;
            document = app.Presentations.Add(0); document.PageSetup.SlideWidth = 640; document.PageSetup.SlideHeight = 480;
            for (int i = 0; i < sources.Length; i++)
            {
                string image = Path.Combine(root, $"office-source-{i:D2}.png"); File.WriteAllBytes(image, sources[i].Png);
                dynamic slide = document.Slides.Add(i + 1, 12);
                slide.Shapes.AddPicture(image, 0, -1, 120, 90, 96, 96);
            }
            var path = Path.Combine(root, "synthetic-heldout.pptx"); document.SaveAs(path, 24); document.Close(); document = null;
            document = app.Presentations.Open(path, -1, 0, 0);
            for (int i = 0; i < sources.Length; i++)
            {
                var output = Path.Combine(root, $"office-export-{i:D2}.png"); document.Slides[i + 1].Export(output, "PNG", 640, 480);
                Add("genuine", "powerpoint-export96", i, sources[i], File.ReadAllBytes(output), new(124, 94, 88, 88));
            }
            officeStatus = "passed";
        }
        catch (Exception ex) { officeStatus = "failed: " + ex.GetBaseException().GetType().Name; }
        finally
        {
            try { if (document is not null) document.Close(); } catch { }
            try { if (app is not null && !existed && (int)app.Presentations.Count == 0) app.Quit(); } catch { }
            if (document is not null && Marshal.IsComObject(document)) Marshal.FinalReleaseComObject(document);
            if (app is not null && Marshal.IsComObject(app)) Marshal.FinalReleaseComObject(app);
        }
    }
    static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; frame.Freeze(); return frame;
    }
    static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var data = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(data, image.PixelWidth * 4, 0); return data;
    }
    static double Overlap(ImageRegion a, ImageRegion b)
    {
        double intersection = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) *
            Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return intersection / ((double)a.Width * a.Height + b.Width * b.Height - intersection);
    }
    static Observation Observe(Input input)
    {
        var bitmap = Decode(File.ReadAllBytes(Path.Combine(root, input.File))); var pixels = Pixels(bitmap);
        var mask = ImageSearch.RedMask(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        var regions = ImageSearch.Detect(mask, bitmap.PixelWidth, bitmap.PixelHeight);
        var selected = regions.Take(30).Select((r, i) => (Region: r, Index: i, Iou: Overlap(r, input.ExpectedBounds)))
            .OrderByDescending(r => r.Iou).FirstOrDefault();
        if (selected.Region is null || selected.Iou < .6) return new(input, regions.Count, -1, false, null, "No accessible detection overlaps the expected stamp", null);
        var region = selected.Region;
        var crop = new CroppedBitmap(bitmap, new Int32Rect(region.X, region.Y, region.Width, region.Height));
        var read = RingCode.Decode(RingCode.RedStrength(Pixels(crop), region.Width, region.Height), region.Width, region.Height, new(0, 0, region.Width, region.Height));
        return new(input, regions.Count, selected.Index, true, read.Code, read.Reason,
            ImageSearch.Normalize(mask, bitmap.PixelWidth, bitmap.PixelHeight, region));
    }
    static void EvaluateHistory(int count, Known[] all, Observation[] observations)
    {
        var folder = Path.Combine(root, "history-" + count); Directory.CreateDirectory(folder);
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(Seed + "/synthetic-key/" + count));
        try
        {
            string previous = "GENESIS"; long sequence = 0;
            foreach (var known in all)
            {
                var entry = new Entry(1, ++sequence, previous, "Generated", known.Generation.EventId,
                    DateTimeOffset.UnixEpoch.AddSeconds(sequence), JsonSerializer.Serialize(known.Generation));
                var text = JsonSerializer.Serialize(entry); var signed = new SignedEntry(text, Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text))));
                File.WriteAllText(Path.Combine(folder, $"{sequence:D12}.json"), JsonSerializer.Serialize(signed)); previous = signed.Mac;
            }
            using var journal = new Journal(folder, key); var service = new VerificationService(journal);
            var history = service.ReadGenerations(); var latest = HistoryReferences.LatestImages(history, 301);
            var templates = latest.Take(300).Select(g =>
            {
                var bitmap = Decode(VerificationService.StoredPng(g)!); var mask = ImageSearch.RedMask(Pixels(bitmap), bitmap.PixelWidth, bitmap.PixelHeight);
                var bounds = ImageSearch.Bounds(mask, bitmap.PixelWidth, bitmap.PixelHeight) ?? throw new InvalidDataException("No reference bounds.");
                return new StampTemplate(g.Stamp, ImageSearch.Normalize(mask, bitmap.PixelWidth, bitmap.PixelHeight, bounds), g.PngSha256);
            }).ToArray();
            foreach (var observation in observations)
            {
                var input = observation.Input; var exact = service.Image(File.ReadAllBytes(Path.Combine(root, input.File)));
                if (exact.Status == VerificationStatus.Indeterminate) throw new InvalidDataException("Synthetic journal could not be authenticated.");
                bool match = exact.Status == VerificationStatus.Match;
                // Exact PNG paths stop in the product. Geometry-only metrics above remain separate research observations.
                var ranked = match || observation.Ink is null ? Array.Empty<VisualCandidate>() : ImageSearch.Rank(observation.Ink,
                    templates.Where(t => !observation.Code.HasValue || t.Stamp.GeometryCode == observation.Code).ToArray(), observation.Code).ToArray();
                double? score = ranked.Length == 0 ? null : CorrespondenceScore.Calculate(ranked[0].Text, observation.Code, ranked[0].Stamp.GeometryCode).Total;
                var candidates = ranked.SelectMany(c => HistoryReferences.Matching(history, c)).Select(g => g.EventId).Distinct().ToArray();
                var top = match ? exact.Matches.Select(g => g.EventId).ToArray() : ranked.Where(c => CorrespondenceScore.Calculate(c.Text, observation.Code, c.Stamp.GeometryCode).Total == score)
                    .SelectMany(c => HistoryReferences.Matching(history, c)).Select(g => g.EventId).Distinct().ToArray();
                bool topContains = input.ExpectedId.HasValue && top.Contains(input.ExpectedId.Value);
                bool green = score.HasValue && ResultIndicator.Candidate(score.Value).Tone == IndicatorTone.Success;
                string topCategory = top.Length == 0 ? "none" : top.Length > 1 ? "tie" : !input.ExpectedId.HasValue ? "negative-candidate" : topContains ? "correct" : "wrong";
                int codeHistory = observation.Code.HasValue ? history.Count(g => g.Stamp.GeometryCode == observation.Code) : 0;
                int codeCandidates = observation.Code.HasValue ? history.Count(g => candidates.Contains(g.EventId) && g.Stamp.GeometryCode == observation.Code) : 0;
                string display = match ? "Exact" : !observation.Detected ? "Unavailable" : count == 0 ? "NoHistory" :
                    VerificationMessages.Image(new(observation.Code, candidates.Length, codeCandidates, codeHistory,
                        templates.Length, observation.Code.HasValue)).Kind.ToString();
                rows.Add(new(count, input.Group, input.Condition, input.Origin, observation.Detected, observation.Regions, observation.SelectedIndex,
                    observation.Code, input.ExpectedCode.HasValue && observation.Code == input.ExpectedCode,
                    input.ExpectedCode.HasValue && observation.Code.HasValue && observation.Code != input.ExpectedCode, !observation.Code.HasValue,
                    exact.Status.ToString(), exact.Matches.Count, input.ExpectedId.HasValue && exact.Matches.Any(g => g.EventId == input.ExpectedId.Value),
                    input.ExpectedId.HasValue && candidates.Contains(input.ExpectedId.Value), topCategory, topContains, top.Length, score, green,
                    match || green, display, templates.Length, latest.Length > 300, input.Sha256));
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    static object Rate(int yes, int total)
    {
        const double z = 1.959963984540054; double p = (double)yes / total, d = 1 + z * z / total;
        double center = (p + z * z / (2 * total)) / d, spread = z * Math.Sqrt(p * (1 - p) / total + z * z / (4 * total * total)) / d;
        return new { Count = yes, Total = total, Rate = p, Wilson95Lower = Math.Max(0, center - spread), Wilson95Upper = Math.Min(1, center + spread) };
    }
    static void Save()
    {
        var groups = rows.GroupBy(r => (r.HistoryCount, r.Group, r.Condition)).Select(group =>
        {
            var a = group.ToArray(); object Measure(Func<Row, bool> condition) => Rate(a.Count(condition), a.Length);
            return new { group.Key.HistoryCount, group.Key.Group, group.Key.Condition, Samples = a.Length,
                Detected = Measure(r => r.Detected), CorrectCode = Measure(r => r.CorrectCode), WrongCode = Measure(r => r.WrongCode), UnreadCode = Measure(r => r.UnreadCode),
                ExactMatch = Measure(r => r.ExactStatus == "Match"), MultipleExact = Measure(r => r.ExactCount > 1), ExactContainsExpected = Measure(r => r.ExactContainsExpected),
                CandidateIncludesExpected = Measure(r => r.CandidatesContainExpected), UniqueTopCorrect = Measure(r => r.TopCategory == "correct"),
                PipelineIncludesExpected = Measure(r => r.ExactContainsExpected || r.CandidatesContainExpected),
                UniqueTopWrong = Measure(r => r.TopCategory == "wrong"), TopTie = Measure(r => r.TopCategory == "tie"), TopSetIncludesExpected = Measure(r => r.TopSetContainsExpected),
                NoCandidate = Measure(r => r.TopCount == 0), Unavailable = Measure(r => r.DisplayKind == "Unavailable"),
                CandidateGreen = Measure(r => r.CandidateGreen), DisplayedGreen = Measure(r => r.DisplayedGreen) };
        }).ToArray();
        var baseline = rows.Where(r => r.HistoryCount == 32).ToArray();
        var native = baseline.Where(r => r.Group == "genuine" && r.Condition == "original384").ToArray();
        var page = baseline.Where(r => r.Group == "genuine" && r.Condition == "page96").ToArray();
        var imitations = baseline.Where(r => r.Group == "imitation").ToArray();
        File.WriteAllText(Path.Combine(root, "evaluation.json"), JsonSerializer.Serialize(new
        {
            Seed, FrozenAlgorithms, Product = typeof(StampRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Os = Environment.OSVersion.VersionString, Utc = DateTimeOffset.UtcNow, OfficeStatus = officeStatus, OfficeVersion = officeVersion,
            Scope = "New controlled synthetic origins and actual PowerPoint export, not a natural-image or print/scan benchmark. Known target position selects among detected first 30 regions; it does not supply a code to decoding or ranking. Exact PNG paths bypass candidates in product.",
            StatisticalUnit = "One new origin per condition. Transforms and history-count repeats are correlated and must not be pooled as independent trials. Wilson intervals are descriptive for this artificial small sample.",
            Acceptance = new
            {
                MeasurementComplete = rows.Select(r => r.HistoryCount).Distinct().Count() == 3 && officeStatus == "passed",
                OriginalExactAll = native.Length == 16 && native.All(r => r.ExactContainsExpected),
                ImitationExactNone = imitations.Length == 32 && imitations.All(r => r.ExactStatus != "Match"),
                NativeDecodeAtLeast95Percent = native.Length == 16 && native.Count(r => r.CorrectCode) / 16.0 >= .95,
                Page96DetectionAtLeast90Percent = page.Length == 16 && page.Count(r => r.Detected) / 16.0 >= .9,
                Page96DecodeAtLeast90Percent = page.Length == 16 && page.Count(r => r.CorrectCode) / 16.0 >= .9
            },
            Summary = groups, Results = rows
        }, Json));
    }
}
