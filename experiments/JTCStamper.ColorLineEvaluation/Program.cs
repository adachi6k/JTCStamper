using System.Text.Json;
using JTCStamper.Core;
if (args.Length == 1 && args[0] == "--check-codec")
{
    long values = 0, flips = 0;
    foreach (var mode in new[] { "ring12", "split12", "split16", "hybrid16" })
        for (int code = 0; code < (1 << ColorLineCodec.PayloadBits(mode)); code++)
        {
            var cells = ColorLineCodec.Encode(code, mode);
            if (ColorLineCodec.Read(cells, mode) != code) throw new Exception("Roundtrip failed");
            values++;
            for (int i = 0; i < cells.Length; i++) { cells[i] = !cells[i]; if (ColorLineCodec.Read(cells, mode).HasValue) throw new Exception("Single-bit corruption accepted"); cells[i] = !cells[i]; flips++; }
        }
    Console.WriteLine(JsonSerializer.Serialize(new { Values = values, SingleBitCorruptionsRejected = flips }));
    return;
}
if (args.Length != 2) throw new ArgumentException("manifest.json report.json or --check-codec");
var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]))!;
var results = new Result[fixtures.Length]; int done = 0;
Parallel.For(0, fixtures.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
{
    var f = fixtures[i]; var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, f.File));
    var regions = ImageSearch.Detect(ImageSearch.RedMask(bytes, f.Width, f.Height), f.Width, f.Height);
    var reading = regions.Count == 1 ? ColorLineCodec.Decode(bytes, f.Width, f.Height, regions[0], f.Mode) : new RingReading(null, null, "Detection failed");
    results[i] = new(f, regions.ToArray(), reading);
    int count = Interlocked.Increment(ref done); if (count % 200 == 0) Console.WriteLine($"{count}/{fixtures.Length}");
});
var summary = results.GroupBy(x => new { x.Fixture.Mode, x.Fixture.Palette, x.Fixture.Background, x.Fixture.Diameter, x.Fixture.Jpeg }).Select(g => new { g.Key, Counts = Stats(g) }).ToArray();
var report = new { Source = "Independent Pillow synthetic RGB; NOT WPF/Office", AuthenticationFalseAcceptanceRate = "NOT EVALUATED", Summary = summary, Results = results };
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var g in results.GroupBy(x => new { x.Fixture.Mode, x.Fixture.Palette })) Console.WriteLine(JsonSerializer.Serialize(new { g.Key, Counts = Stats(g) }));
static object Stats(IEnumerable<Result> items)
{
    var a = items.ToArray(); var encoded = a.Where(x => x.Fixture.Kind == "encoded").ToArray();
    return new { Encoded = encoded.Length, Correct = encoded.Count(x => x.Reading.Code == x.Fixture.Expected), Wrong = encoded.Count(x => x.Reading.Code.HasValue && x.Reading.Code != x.Fixture.Expected), Unreadable = encoded.Count(x => !x.Reading.Code.HasValue), Controls = a.Length - encoded.Length, ControlClassified = a.Count(x => x.Fixture.Kind != "encoded" && x.Reading.Code.HasValue) };
}
record Fixture(string File, int Width, int Height, string Mode, string Palette, string Background, string Name, string Kind, int? Expected, int Diameter, int Rotation, double Offset, bool Jpeg);
record Result(Fixture Fixture, ImageRegion[] Regions, RingReading Reading);
