using System.Text.Json;
using JTCStamper.Core;

if (args.Length != 2) throw new ArgumentException("Usage: manifest.json report.json");
var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]))!;
var results = new List<object>(); int correct = 0, wrong = 0, unreadable = 0, controlClassified = 0;
foreach (var item in fixtures)
{
    var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, item.File));
    var red = GeometryCode.RedStrength(bytes, item.Width, item.Height);
    var mask = ImageSearch.RedMask(bytes, item.Width, item.Height);
    var regions = ImageSearch.Detect(mask, item.Width, item.Height);
    GeometryReading reading = new(null, null, null, "Detection failed", 12);
    GeometryReading? angles = null;
    if (regions.Count == 1)
    {
        angles = GeometryCode.Decode(red, item.Width, item.Height, regions[0]);
        reading = RingCode.Decode(red, item.Width, item.Height, regions[0], angles);
    }
    if (item.Kind == "encoded")
    {
        if (reading.Code == item.Expected) correct++;
        else if (reading.Code is null) unreadable++;
        else wrong++;
    }
    else if (reading.Code.HasValue) controlClassified++;
    results.Add(new { Fixture = item, Regions = regions.Count, Bounds = regions, AngleReading = angles, Reading = reading });
}
var report = new { Source = "Independent Pillow synthetic raster; NOT WPF/Office", Encoded = fixtures.Count(x => x.Kind == "encoded"), Correct = correct, Wrong = wrong, Unreadable = unreadable,
    Controls = fixtures.Count(x => x.Kind != "encoded"), ControlClassified = controlClassified,
    AuthenticationFalseAcceptanceRate = "NOT EVALUATED: code classification is not authentication", Results = results };
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"correct={correct}, wrong={wrong}, unreadable={unreadable}, control classified={controlClassified}; {args[1]}");
return wrong == 0 ? 0 : 1;
record Fixture(string File, int Width, int Height, string Kind, int? Expected, string Name, int Diameter, int Rotation, double Offset, bool Jpeg);
