using System.Text.Json;
using JTCStamper.Core;
if(args.Length!=2) throw new ArgumentException("manifest.json report.json");
var fixtures=JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]))!;
var results=new List<Result>();
foreach(var f in fixtures) {
 var bytes=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,f.File));
 var regions=ImageSearch.Detect(ImageSearch.RedMask(bytes,f.Width,f.Height),f.Width,f.Height);
 int? code=null; int candidates=0;
 if(regions.Count==1) {
  var red=RingCode.RedStrength(bytes,f.Width,f.Height);
  code=f.Bits==12 ? RingCode.Decode(red,f.Width,f.Height,regions[0]).Code : ImageDecoder.Decode(red,f.Width,f.Height,regions[0],f.Bits,out candidates);
 }
 results.Add(new(f,regions.Count,code,f.Bits==12?null:candidates));
 if(results.Count%100==0)Console.WriteLine($"evaluated {results.Count}/{fixtures.Length}");
}
var summary=results.GroupBy(r=>new{r.Fixture.Bits,r.Fixture.Kind,r.Fixture.Transform,r.Fixture.Split}).Select(g=>new{g.Key,Total=g.Count(),Correct=g.Count(r=>r.Fixture.Kind=="encoded"&&r.Decoded==r.Fixture.Expected),Wrong=g.Count(r=>r.Fixture.Kind=="encoded"&&r.Decoded.HasValue&&r.Decoded!=r.Fixture.Expected),Unreadable=g.Count(r=>!r.Decoded.HasValue),ControlClassified=g.Count(r=>r.Fixture.Kind!="encoded"&&r.Decoded.HasValue)});
File.WriteAllText(args[1],JsonSerializer.Serialize(new{Source="Pillow synthetic / automatic detection / known format / no history-assisted decoding / no ECC",Summary=summary,Results=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Finished");
record Fixture(string File,int Width,int Height,int Bits,string Kind,int? Expected,string Transform,string Split,int Diameter,double Rotation,double Offset);
record Result(Fixture Fixture,int Regions,int? Decoded,int? CandidateCodesCount);
