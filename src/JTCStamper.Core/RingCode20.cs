using System.Security.Cryptography;

namespace JTCStamper.Core;
public static class RingCode20
{
    public const string Renderer = "wpf-v6-gap20";
    public const int MaxValue = 0xFFFFF;
    public const int RingCells = 32;
    public static int ForEvent(Guid id)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        return (hash[0] | hash[1] << 8 | hash[2] << 16) & MaxValue;
    }
    public static bool[] Encode(int code)
    {
        if(code < 0 || code > MaxValue) throw new ArgumentOutOfRangeException(nameof(code));
        ulong word = ((ulong)0xD3 << 28) | ((ulong)code << 8) | Checksum(code,20);
        return Enumerable.Range(0,36).Select(i => ((word >> (35-i)) & 1) != 0).ToArray();
    }
    public static double CellAngle(int index)
    {
        if(index < 0 || index >= RingCells) throw new ArgumentOutOfRangeException(nameof(index));
        return Angle(index,RingCells);
    }
    public static float[] Ink(byte[] bgra, int width, int height)
    {
        if(width <= 0 || height <= 0 || (long)width*height > 12_000_000 || (long)width*height*4 != bgra.Length) throw new ArgumentException();
        var result = new float[width*height];
        for(int i=0;i<result.Length;i++) { int p=i*4; result[i]=bgra[p+2]>bgra[p+1]+10 ? (255-bgra[p+1])/255f*bgra[p+3]/255f : 0; }
        return result;
    }

    public sealed record Hit(int Code, double CenterX, double CenterY, double Rotation, int Delta, double MarkerContrast);

    public static int? Decode(float[] red, int width, int height, ImageRegion box, CancellationToken cancellationToken = default)
    {
        const int bits = 20;
        var Hits = new List<Hit>();
        if(width <= 0 || height <= 0 || (long)width*height != red.Length || (long)width*height > 12_000_000 ||
            box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 || (long)box.X+box.Width>width || (long)box.Y+box.Height>height) throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (box.Width < 80 || box.Height < 80 || Math.Abs((double)box.Width / box.Height - 1) > 0.04) return null;
        // Fit intact portions of both separators before looking at payload or CRC.
        double bestScore = double.NegativeInfinity, fitRotation = 0; int fitDelta = 0;
        for(double rot=-8;rot<=8;rot+=0.25)
        foreach(int delta in new[]{-3,-1,1,3}) {
            cancellationToken.ThrowIfCancellationRequested();
            double score=0;
            for(int row=0;row<2;row++) {
                double rowBest=double.NegativeInfinity;
                for(double shift=-1.5;shift<=1.5;shift+=0.25) {
                    double rowScore=0;
                    foreach(int t in new[]{-34,-32,-30,-18,-16,-14,14,16,18,30,32,34}) {
                        double a=(row==0?delta:-delta)/2.0*Math.PI/180;
                        double lx=t*Math.Cos(a), ly=(row==0?-15:15)+t*Math.Sin(a);
                        double r=rot*Math.PI/180;
                        double x=box.X+(box.Width-1)/2.0+(lx*Math.Cos(r)-ly*Math.Sin(r))*box.Width/87.1;
                        double y=box.Y+(box.Height-1)/2.0+(lx*Math.Sin(r)+ly*Math.Cos(r))*box.Height/87.1+shift;
                        rowScore+=Sample(red,width,height,x,y);
                    }
                    rowBest=Math.Max(rowBest,rowScore);
                }
                score+=rowBest;
            }

            if(score>bestScore){bestScore=score;fitRotation=rot;fitDelta=delta;}
        }
        bool split = true;
        int count = bits + 12;
        var accepted = new HashSet<int>();
        // Search a bounded orientation range without using a known payload or history.
        foreach (double centerX in new[] { -0.5, 0.0, 0.5 })
        foreach (double centerY in new[] { -0.5, 0.0, 0.5 })
        for (double rotation = -8; rotation <= 8; rotation += 0.25)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(Math.Abs(rotation-fitRotation)>0.5) continue;
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                double value = 0;
                for (int offset = -1; offset <= 1; offset++)
                {
                    double a = (Angle(i, count) + rotation + offset * 0.3) * Math.PI / 180;
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
                if (((0xD3 >> (7 - i)) & 1) == 0) { on += values[i]; onCount++; }
                else { off += values[i]; offCount++; }
            }
            on /= onCount; off /= offCount;
            if (on < 0.16 || on - off < 0.12 || off > on * 0.50) continue;
            double threshold = (on + off) / 2, margin = (on - off) * 0.12;
            // Marker stays exact, with the original confidence margin. Payload is hard-decoded.
            if (Enumerable.Range(0, 8).Any(i => Math.Abs(values[i] - threshold) < margin ||
                (values[i] < threshold) != (((0xD3 >> (7-i)) & 1) != 0))) continue;
            uint prefix = 0;
            for (int i = 8; i < count; i++) prefix = (prefix << 1) | (values[i] < threshold ? 1u : 0);
            foreach (int delta in split ? new[] { -3, -1, 1, 3 } : new[] { 0 })
            {

                if(delta != fitDelta) continue;
                uint word = prefix;
                if (split)
                {
                    for (int row = 0; row < 2; row++) foreach (int t in new[] { -24, 24 })
                    {
                        double value = 0;
                        for (int along = -1; along <= 1; along++)
                        {
                            double peak = 0;
                            for (double normal = -0.8; normal <= 0.8; normal += 0.4)
                            {
                                double lineAngle = (row == 0 ? delta : -delta) / 2.0 * Math.PI / 180;
                                double lx = (t + along * 0.4) * Math.Cos(lineAngle) - normal * Math.Sin(lineAngle);
                                double ly = (row == 0 ? -15 : 15) + (t + along * 0.4) * Math.Sin(lineAngle) + normal * Math.Cos(lineAngle);
                                double r = rotation * Math.PI / 180;
                                double x = box.X + (box.Width-1)/2.0 + centerX + (lx*Math.Cos(r)-ly*Math.Sin(r))*box.Width/87.1;
                                double y = box.Y + (box.Height-1)/2.0 + centerY + (lx*Math.Sin(r)+ly*Math.Cos(r))*box.Height/87.1;
                                peak = Math.Max(peak, Sample(red, width, height, x, y));
                            }
                            value += peak;
                        }
                        word = (word << 1) | (value/3 < threshold ? 1u : 0);
                    }
                }
                int code = (int)(word >> 8);
                if (code >= (1 << bits) || (word & 255) != Checksum(code, bits)) continue;
                if ((code & 3)*2-3 != delta) continue;
                accepted.Add(code); Hits.Add(new Hit(code,centerX,centerY,rotation,delta,on-off));
            }
        }
        // Resolve only isolated one-hit conflicts against a geometrically stable candidate.
        // These correlated samples are NOT independent probability estimates.
        // The reference payload and history are never consulted.

        if(accepted.Count == 1) return accepted.Single();
        var groups=Hits.GroupBy(h=>h.Code).ToArray();
        var stable=groups.Where(g=>g.Select(h=>(h.CenterX,h.CenterY)).Distinct().Count()>=6 &&
            g.Select(h=>h.Rotation).Distinct().Count()>=2).ToArray();
        if(stable.Length==1 && groups.Where(g=>g.Key!=stable[0].Key).All(g=>g.Count()==1))
            return stable[0].Key;
        return null;
    }
    static byte Checksum(int code, int bits)
    {
        byte crc = 0;
        var bytes = new List<byte> { 0xD3, (byte)bits };
        for (int shift = ((bits+7)/8-1)*8; shift >= 0; shift -= 8) bytes.Add((byte)(code >> shift));
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (byte)((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0));
        }
        return crc;
    }
    static double Angle(int index, int count) => (index < count/2 ? 37 : 217) + (index % (count/2))*106.0/(count/2-1);

    static double Sample(float[] red, int width, int height, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix + 1 >= width || iy + 1 >= height) return 0;
        double fx = x - ix, fy = y - iy;
        return red[iy * width + ix] * (1 - fx) * (1 - fy) + red[iy * width + ix + 1] * fx * (1 - fy)
            + red[(iy + 1) * width + ix] * (1 - fx) * fy + red[(iy + 1) * width + ix + 1] * fx * fy;
    }
}
