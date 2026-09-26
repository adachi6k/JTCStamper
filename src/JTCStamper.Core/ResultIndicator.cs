using System.Globalization;
namespace JTCStamper.Core;

public enum IndicatorTone { Success, Caution, NoCandidate }
public sealed record ResultIndicator(IndicatorTone Tone, string Value, string Label)
{
    public string Explanation { get; init; } = "";
    public static ResultIndicator Candidate(double score)
    {
        if (!double.IsFinite(score) || score < 0 || score > 1) throw new ArgumentOutOfRangeException(nameof(score));
        // Rounded-up values must never be promoted to green.
        bool maximum = score == 1;
        string value = !maximum && score >= .9995 ? "< 1.000" : score.ToString("0.000", CultureInfo.InvariantCulture);
        return new(maximum ? IndicatorTone.Success : IndicatorTone.Caution, value,
            maximum ? "✓ 履歴候補とのスコア最大" : "⚠ 似ている履歴の候補")
        { Explanation = "1.000は、文字の形とコードが履歴候補と一致した点数です。\n画像全体の一致とは別です。" };
    }
    public static ResultIndicator Exact(bool original = false) => new(IndicatorTone.Success, "一致", "✓ 保存された生成記録と一致")
    { Explanation = original ? "原本の記録ID・認証情報・内容が、保存された生成記録と一致しました。" : "PNGファイル全体が、生成時に保存した画像と一致しました。" };
    public static ResultIndicator Empty(bool noCandidate) => new(noCandidate ? IndicatorTone.NoCandidate : IndicatorTone.Caution,
        "—", noCandidate ? "× 一致する履歴候補なし" : "⚠ 判定不能・手がかり不足")
        { Explanation = noCandidate ? "今回の検索では候補を見つけられませんでした。偽造という意味ではありません。" : "履歴の有無を判断できていません。" };
}
