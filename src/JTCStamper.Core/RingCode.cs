namespace JTCStamper.Core;

public sealed record RingReading(int? Code, double? RotationDegrees, string Reason);

// Product format: 20-bit lookup value, marker8 + CRC8. None is authentication.
public static class RingCode
{
    public const string PlainRenderer = "wpf-plain-v1";
    public const string Renderer = RingCode20.Renderer;
    public const int CellCount = RingCode20.RingCells;
    public const double GapDegrees = 3.5;
    public static string Label(int code) => Convert.ToString(code,2).PadLeft(20,'0');
    public static double SeparatorDifference(int code) => (code & 3)*2-3;
    public static int ForEvent(Guid id) => RingCode20.ForEvent(id);
    public static bool[] Encode(int code) => RingCode20.Encode(code);
    public static double CellAngle(int index) => RingCode20.CellAngle(index);
    public static int? ReadCells(IReadOnlyList<bool> cells)
    {
        if(cells.Count != 36) throw new ArgumentException();
        ulong word=0; foreach(bool cell in cells) word=(word<<1)|(cell?1UL:0UL);
        int code=(int)((word>>8)&RingCode20.MaxValue);
        return cells.SequenceEqual(Encode(code)) ? code : null;
    }
    public static void Validate(Stamp stamp)
    {
        if(stamp.Renderer == PlainRenderer)
        {
            if(stamp.GeometryCode is not null) throw new InvalidDataException("プレーン印影に幾何コードは指定できません。");
            return;
        }
        if(stamp.Renderer != Renderer) throw new NotSupportedException("20ビット形式とプレーン形式に対応しています。");
        if(stamp.GeometryCode is null or < 0 or > RingCode20.MaxValue) throw new InvalidDataException("20ビットの幾何コードが不正です。");
    }
    public static float[] RedStrength(byte[] bgra,int width,int height) => RingCode20.Ink(bgra,width,height);
    public static RingReading Decode(float[] red,int width,int height,ImageRegion box,CancellationToken cancellationToken=default)
    {
        var code=RingCode20.Decode(red,width,height,box,cancellationToken);
        return new(code,null,code.HasValue?"20bitの履歴候補コードです。":"20bitコードを確定できません。印影全体の範囲と解像度を確認してください。");
    }
}
