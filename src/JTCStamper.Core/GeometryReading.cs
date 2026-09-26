namespace JTCStamper.Core;

public sealed record GeometryReading(int? Code, string? Renderer, string Reason)
{
    public bool Matches(Stamp stamp) => Code.HasValue && stamp.Renderer == Renderer && stamp.GeometryCode == Code;
    public int Bits => 20;
    public string Label => Code is int code ? RingCode.Label(code) : "";
    public static GeometryReading Read(byte[] bgra,int width,int height,ImageRegion box,CancellationToken token=default)
    {
        var code=RingCode20.Decode(RingCode20.Ink(bgra,width,height),width,height,box,token);
        return new(code,code.HasValue?RingCode20.Renderer:null,code.HasValue?"20bitの履歴候補コードを読み取りました。":"20bitコードを確定できません。印影全体の範囲と解像度を確認してください。");
    }
}
