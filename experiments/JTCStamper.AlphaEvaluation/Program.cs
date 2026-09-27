using System.Text.Json;
using JTCStamper.Core;
if (args.Length != 2) throw new ArgumentException("manifest.json report.json");
var fixtures=JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]))!;
var results=new Result[fixtures.Length]; int done=0;
Parallel.For(0,fixtures.Length,new ParallelOptions { MaxDegreeOfParallelism=8 },i=>
{
    var f=fixtures[i];var bytes=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,f.File));
    var red=RingCode.RedStrength(bytes,f.Width,f.Height);var mask=ImageSearch.RedMask(bytes,f.Width,f.Height);
    var regions=ImageSearch.Detect(mask,f.Width,f.Height);
    RingReading baseline=new(null,null,"Detection failed"),experimental=baseline,sensitive=baseline;
    if(regions.Count==1) { baseline=RingCode.Decode(red,f.Width,f.Height,regions[0]);experimental=ExperimentalAlphaDecoder.Decode(red,f.Width,f.Height,regions[0]);sensitive=ExperimentalAlphaDecoder.Decode(red,f.Width,f.Height,regions[0],0.03,0.95); }
    results[i]=new(f,regions.ToArray(),baseline,experimental,sensitive);
    int count=Interlocked.Increment(ref done);if(count%200==0)Console.WriteLine($"{count}/{fixtures.Length}");
});
var summary=results.GroupBy(x=>new{x.Fixture.Opacity,x.Fixture.Background,x.Fixture.Diameter,x.Fixture.Jpeg}).Select(g=>new {g.Key,Baseline=Stats(g,0),Experimental=Stats(g,1),Sensitive=Stats(g,2)}).ToArray();
var report=new{Source="Independent Pillow synthetic RGBA, composited before PNG/JPEG; NOT WPF/Office",Experimental="Marker contrast thresholds: production 0.12/0.50, experimental 0.08/0.85, sensitive 0.03/0.95; fixed across opacity/background, sensitive added after first JPEG results",AuthenticationFalseAcceptanceRate="NOT EVALUATED",Summary=summary,Results=results};
File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
foreach(var g in results.GroupBy(x=>x.Fixture.Opacity))Console.WriteLine(JsonSerializer.Serialize(new{Opacity=g.Key,Baseline=Stats(g,0),Experimental=Stats(g,1),Sensitive=Stats(g,2)}));
static object Stats(IEnumerable<Result> items,int profile)
{
 var a=items.ToArray();var encoded=a.Where(x=>x.Fixture.Kind=="encoded").ToArray();
 int? Code(Result r)=>(profile==2?r.Sensitive:profile==1?r.Experimental:r.Baseline).Code;
 return new{Encoded=encoded.Length,Correct=encoded.Count(x=>Code(x)==x.Fixture.Expected),Wrong=encoded.Count(x=>Code(x).HasValue && Code(x)!=x.Fixture.Expected),Unreadable=encoded.Count(x=>!Code(x).HasValue),Controls=a.Length-encoded.Length,ControlClassified=a.Count(x=>x.Fixture.Kind!="encoded" && Code(x).HasValue)};
}
record Fixture(string File,int Width,int Height,string Kind,int? Expected,string Name,int Diameter,int Rotation,double Offset,bool Jpeg,double Opacity,string Background);
record Result(Fixture Fixture,ImageRegion[] Regions,RingReading Baseline,RingReading Experimental,RingReading Sensitive);
