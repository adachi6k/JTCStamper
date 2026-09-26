using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Media;
using JTCStamper.App;
using JTCStamper.Core;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--copy-emf") { EmfClipboard.Copy(args[1]); return 0; }
        if (args.Length != 1) { Console.Error.WriteLine("Usage: JTCStamper.EmfEvaluation OUTPUT_DIRECTORY (no clipboard changes)"); return 2; }
        Directory.CreateDirectory(args[0]);
        // Regression: WPF allows a missing Transform (identity), including nested groups.
        var nullScene = new DrawingGroup { Transform = null };
        var nested = new DrawingGroup { Transform = null };
        nested.Children.Add(new GeometryDrawing(Brushes.Red, null,
            new RectangleGeometry(new System.Windows.Rect(5, 5, 10, 10)) { Transform = null }));
        nested.Children.Add(new GeometryDrawing()); // Empty drawing is a valid no-op.
        nullScene.Children.Add(nested);
        nullScene.Freeze();
        foreach (var style in Enum.GetValues<VectorEmf.Appearance>())
            if (VectorEmf.Export(nullScene, style).Length == 0)
                throw new InvalidDataException("Null-transform regression failed.");
        Console.WriteLine("Null-transform/empty-geometry regression passed.");
        var results = new List<object>();
        foreach (var (name, index) in new[] { "", "佐藤", "山田太郎", "JTC", "𠮷田", "長い上段文字の確認" }.Select((n,i)=>(n,i)))
        foreach (bool plain in new[] { false, true })
        {
            int code = new[] { 0, 1, 0x55555, 0xAAAAA, 0xABCDE, 0xFFFFF }[index];
            var stamp = new Stamp(name, new DateOnly(2026, 9, 27), "(印)",
                plain ? RingCode.PlainRenderer : RingCode.Renderer, plain ? null : code);
            string stem = $"{index}-{(plain ? "plain" : "coded")}";
            var scene = StampRenderer.CreateDrawing(stamp, new Typeface("Yu Gothic"));
            File.WriteAllBytes(Path.Combine(args[0], stem+"-reference.png"), StampRenderer.Png(StampRenderer.Render(stamp)));
            foreach (var style in Enum.GetValues<VectorEmf.Appearance>())
            {
                byte[] bytes = VectorEmf.Export(scene, style);
                var types = new HashSet<uint>(); int pos=0;
                while (pos < bytes.Length)
                {
                    if (pos+8>bytes.Length) throw new InvalidDataException("Truncated EMF record.");
                    uint type = BitConverter.ToUInt32(bytes,pos), size = BitConverter.ToUInt32(bytes,pos+4);
                    if (size<8 || size%4!=0 || size>bytes.Length-pos) throw new InvalidDataException("Invalid EMF record length.");
                    types.Add(type);pos+=checked((int)size);
                }
                // EMR bitmap operations, AlphaBlend, TransparentBlt, plus GDI comments (e.g. EMF+).
                uint[] forbidden = [70,76,77,78,79,80,81,93,94,114,116];
                if (types.Overlaps(forbidden)) throw new InvalidDataException("Unexpected raster/opaque extension record.");
                if (!types.Contains(1) || !types.Contains(14) || !types.Contains(62)) throw new InvalidDataException("Missing header/EOF/vector fill.");
                string file=stem+"-"+style+".emf"; File.WriteAllBytes(Path.Combine(args[0],file),bytes);
                results.Add(new { File=file, Code=plain?(int?)null:code, Appearance=style.ToString(), Bytes=bytes.Length,
                    Sha256=Convert.ToHexString(SHA256.HashData(bytes)), Records=types.Order().ToArray(), RasterRecords=0 });
            }
        }
        File.WriteAllText(Path.Combine(args[0],"report.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Generated {results.Count} vector EMF fixtures. Office/clipboard behavior is NOT tested by this command.");
        return 0;
    }
}
