using System.Globalization;
namespace JTCStamper.Core;

public enum IndicatorTone { Success, Caution, NoCandidate }
public sealed record ResultIndicator(IndicatorTone Tone, string Value, string Label)
{
    public static ResultIndicator Candidate(double score)
    {
        if (!double.IsFinite(score) || score < 0 || score > 1) throw new ArgumentOutOfRangeException(nameof(score));
        // Rounded-up values must never be promoted to green.
        bool maximum = score == 1;
        string value = !maximum && score >= .9995 ? "< 1.000" : score.ToString("0.000", CultureInfo.InvariantCulture);
        return new(maximum ? IndicatorTone.Success : IndicatorTone.Caution, value,
            maximum ? "✓ OK・対応スコア最大（候補）" : "⚠ 注意・追加確認が必要");
    }
    public static ResultIndicator Exact() => new(IndicatorTone.Success, "一致", "✓ OK・生成記録との完全一致");
    public static ResultIndicator Empty(bool noCandidate) => new(noCandidate ? IndicatorTone.NoCandidate : IndicatorTone.Caution,
        "—", noCandidate ? "× 一致する履歴候補なし" : "⚠ 判定不能・手がかり不足");
}
