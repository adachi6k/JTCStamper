using System.ComponentModel;
using System.IO;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

// Research only. Never silently claim parity with the textured transparent PNG.
internal static class VectorEmf
{
    internal enum Appearance { Crisp, WhitePaperApproximation }
    internal static byte[] Export(Drawing drawing, Appearance appearance)
    {
        // Outer stroked circle diameter = 87.1 design units. Give it a physical 15mm diameter.
        int frameSize = (int)Math.Round(1500.0 * 96 / 87.1);
        var frame = new RectL(0, 0, frameSize, frameSize);
        nint dc = CreateEnhMetaFileW(0, null, ref frame, null);
        Require(dc != 0, "CreateEnhMetaFile");
        nint emf = 0;
        try
        {
            // MM_HIMETRIC: logical units are 0.01mm; negate Y for top-to-bottom drawing.
            Require(SetMapMode(dc, 3) != 0, "SetMapMode");
            Visit(drawing);
            emf = CloseEnhMetaFile(dc); dc = 0;
            Require(emf != 0, "CloseEnhMetaFile");
            uint length = GetEnhMetaFileBits(emf, 0, null);
            Require(length > 0 && length < 32 * 1024 * 1024, "EMF size");
            var bytes = new byte[length];
            Require(GetEnhMetaFileBits(emf, length, bytes) == length, "GetEnhMetaFileBits");
            return NormalizeCoordinates(bytes);
        }
        finally
        {
            if (dc != 0) { var unfinished = CloseEnhMetaFile(dc); if (unfinished != 0) DeleteEnhMetaFile(unfinished); }
            if (emf != 0) DeleteEnhMetaFile(emf);
        }

        void Visit(Drawing item)
        {
            if (item is DrawingGroup group)
            {
                if (group.ClipGeometry != null || (group.Transform is { } transform && !transform.Value.IsIdentity) || group.Opacity != 1)
                    throw new NotSupportedException("Unexpected scene transform/clip/opacity.");
                // Explicit experiment policies: omit texture or approximate its mean on white.
                foreach (var child in group.Children) Visit(child);
                return;
            }
            if (item is not GeometryDrawing shape) throw new NotSupportedException("Only vector geometry is permitted.");
            if (shape.Geometry == null || shape.Geometry.IsEmpty()) return;
            if (shape.Brush != null) Fill(shape.Geometry, shape.Brush);
            if (shape.Pen?.Brush != null)
                Fill(shape.Geometry.GetWidenedPathGeometry(shape.Pen, .005, ToleranceType.Absolute), shape.Pen.Brush);
        }
        void Fill(Geometry geometry, Brush brush)
        {
            if (brush is not SolidColorBrush solid) throw new NotSupportedException("Only solid vector fills are permitted.");
            double opacity = solid.Opacity * solid.Color.A / 255.0;
            if (appearance == Appearance.Crisp && opacity < .999) return;
            double amount = appearance == Appearance.Crisp ? 1 : opacity * .9;
            byte Channel(byte value) => (byte)Math.Round(255 + (value - 255) * amount);
            uint color = (uint)(Channel(solid.Color.R) | Channel(solid.Color.G) << 8 | Channel(solid.Color.B) << 16);
            nint fill = CreateSolidBrush(color); Require(fill != 0, "CreateSolidBrush");
            nint old = 0;
            try
            {
                old = SelectObject(dc, fill); Require(old != 0 && old != -1, "SelectObject");
                var path = geometry.GetFlattenedPathGeometry(.005, ToleranceType.Absolute);
                Require(SetPolyFillMode(dc, path.FillRule == FillRule.Nonzero ? 2 : 1) != 0, "SetPolyFillMode");
                Require(BeginPath(dc), "BeginPath");
                foreach (var figure in path.Figures)
                {
                    if (!figure.IsFilled) continue;
                    var first = Convert(figure.StartPoint);
                    Require(MoveToEx(dc, first.X, first.Y, 0), "MoveToEx");
                    foreach (var segment in figure.Segments)
                    {
                        if (segment is PolyLineSegment poly) foreach (var p in poly.Points) Line(p);
                        else if (segment is LineSegment line) Line(line.Point);
                        else throw new NotSupportedException("Flattened path contained a curve.");
                    }
                    Require(CloseFigure(dc), "CloseFigure");
                }
                Require(EndPath(dc), "EndPath"); Require(FillPath(dc), "FillPath");
                (int X, int Y) Convert(Point p)
                {
                    p = path.Transform?.Transform(p) ?? p;
                    return ((int)Math.Round(p.X * frameSize / 96), -(int)Math.Round(p.Y * frameSize / 96));
                }
                void Line(Point p) { var point = Convert(p); Require(LineTo(dc, point.X, point.Y), "LineTo"); }
            }
            finally { if (old != 0 && old != -1) SelectObject(dc, old); DeleteObject(fill); }
        }
    }
    // Canonical MM_TEXT coordinates on a virtual 100px/mm device. Each path unit
    // remains 0.01mm without relying on an Office consumer's MM_HIMETRIC replay.
    // Research fix: Office/PDF roundtrip still requires validation.
    private static byte[] NormalizeCoordinates(byte[] bytes)
    {
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        var fills = new List<int>();
        int minX=int.MaxValue, minY=int.MaxValue, maxX=int.MinValue, maxY=int.MinValue;
        for (int offset=0; offset<bytes.Length;)
        {
            if (offset+8>bytes.Length) throw new InvalidDataException("Truncated EMF.");
            int type=Read(offset), size=Read(offset+4);
            if (size<8 || size%4!=0 || size>bytes.Length-offset) throw new InvalidDataException("Invalid EMF record.");
            if (type is not (1 or 14 or 17 or 19 or 27 or 37 or 39 or 40 or 54 or 59 or 60 or 61 or 62))
                throw new NotSupportedException($"Coordinate normalizer cannot handle EMR {type}.");
            if (type is 27 or 54)
            {
                if (size<16) throw new InvalidDataException("Truncated point.");
                int x=Read(offset+8), y=checked(-Read(offset+12));
                Write(offset+12,y);
                minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
            }
            if (type==17)
            {
                if (size<12 || Read(offset+8)!=3) throw new InvalidDataException("Expected MM_HIMETRIC input.");
                Write(offset+8,1);
            }
            if (type==62) { if(size<24) throw new InvalidDataException("Truncated FillPath."); fills.Add(offset+8); }
            offset+=size;
        }
        if (Read(0)!=1 || Read(4)<108 || minX==int.MaxValue) throw new InvalidDataException("Missing EMF header/geometry.");
        void Bounds(int offset)
        {
            Write(offset,minX); Write(offset+4,minY); Write(offset+8,checked(maxX+1)); Write(offset+12,checked(maxY+1));
        }
        Bounds(8);
        foreach (int offset in fills) Bounds(offset); // conservative bounds encompass every path
        Write(72,10000); Write(76,10000); Write(80,100); Write(84,100);
        Write(100,100000); Write(104,100000);
        return bytes;
    }

    private static void Require(bool ok, string name) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), name); }
    [StructLayout(LayoutKind.Sequential)] private struct RectL(int l, int t, int r, int b) { public int Left=l, Top=t, Right=r, Bottom=b; }
    [DllImport("gdi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint CreateEnhMetaFileW(nint reference, string? file, ref RectL frame, string? description);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern nint CloseEnhMetaFile(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(nint emf);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern uint GetEnhMetaFileBits(nint emf, uint size, byte[]? bytes);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int SetMapMode(nint dc, int mode);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool BeginPath(nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool EndPath(nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool CloseFigure(nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool FillPath(nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int SetPolyFillMode(nint dc, int mode);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool MoveToEx(nint dc, int x, int y, nint previous);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool LineTo(nint dc, int x, int y);
}
