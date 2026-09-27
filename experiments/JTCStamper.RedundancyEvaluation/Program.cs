using System.Text.Json;
using JTCStamper.Core;
if (args.Length == 1 && args[0] == "--check-codec")
{
    Console.WriteLine(JsonSerializer.Serialize(Golay.Check())); return;
}
if (args.Length != 2) throw new ArgumentException("manifest.json report.json | --check-codec");
var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]))!;
var results = new List<Result>();
foreach (var f in fixtures)
{
    var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, f.File));
    var regions = ImageSearch.Detect(ImageSearch.RedMask(bytes,f.Width,f.Height),f.Width,f.Height);
    int?[] readings = new int?[f.Mode == "crc28" ? 1 : 4];
    if (regions.Count == 1)
    {
        var red = RingCode.RedStrength(bytes,f.Width,f.Height);
        readings = f.Mode == "crc28" ? new[] { RingCode.Decode(red,f.Width,f.Height,regions[0]).Code }
            : ImageDecoder.Decode(red,f.Width,f.Height,regions[0],f.Mode);
    }
    for (int limit = 0; limit < readings.Length; limit++) results.Add(new(f,limit,regions.Count,readings[limit]));
    if (results.Count % 100 == 0) Console.WriteLine($"evaluated {f.File}");
}
var summary = results.GroupBy(r => new {r.Fixture.Mode,r.Fixture.Kind,r.Fixture.Transform,r.Limit})
    .Select(g => new {g.Key,Total=g.Count(),Correct=g.Count(r=>r.Fixture.Kind=="encoded" && r.Decoded==r.Fixture.Expected),
        Wrong=g.Count(r=>r.Fixture.Kind=="encoded" && r.Decoded.HasValue && r.Decoded!=r.Fixture.Expected),
        Unreadable=g.Count(r=>!r.Decoded.HasValue),ControlClassified=g.Count(r=>r.Fixture.Kind!="encoded" && r.Decoded.HasValue)}).ToArray();
File.WriteAllText(args[1],JsonSerializer.Serialize(new {Source="Synthetic Pillow images, automatic crop detection, no history-assisted decoding; NOT WPF/Office",Codec=Golay.Check(),Summary=summary,Results=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Finished {fixtures.Length} images; {args[1]}");
record Fixture(string File,int Width,int Height,string Mode,string Kind,int? Expected,string Transform,int Diameter,double Rotation,double Offset);
record Result(Fixture Fixture,int Limit,int Regions,int? Decoded);
