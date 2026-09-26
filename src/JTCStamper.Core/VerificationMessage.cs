using System.Globalization;

namespace JTCStamper.Core;

public enum VerificationMessageKind { Exact, Candidate, NoCandidate, Unavailable }
public sealed record VerificationMessage(VerificationMessageKind Kind, string Title, string Findings, string Meaning, string NextStep);
public sealed record ImageSearchEvidence(int? Code, int CandidateCount, int CodeMatchedCandidates,
    int CodeHistoryCount, int ComparedStampCount, bool FilterEnabled);

// Presentation only: a visual candidate must never be promoted to an exact/authenticated match.
public static class VerificationMessages
{
    public const string UsageLimit = "保存画像をそのままコピーしたものは見分けられません。";
    public const string ScoreHelp = "履歴との対応スコア（暫定）は確率ではありません。文字の形は最大0.300、コード一致は0.700です。1.000でも原本・PNGの完全一致や真正性を意味しません。";
    public const string MethodHelp = "上段文字・日付・下段文字の領域を別々に比較し、それぞれ最大0.100を加点します。96×96にそろえ、±8度の回転と1画素のずれを許容します。文字の意味を読むOCRではなく、形の比較です。空白や画素不足の領域には加点しません。\nコードは位置マーカー・CRC検査を通過して読み取れた12ビットが履歴と一致した場合に0.700、不一致・未読取は0です。読取信頼度の連続評価や部分一致への加点は行いません。CRCや冗長情報は別の証拠として加点しません。\n配点は暫定で、確率としての校正はしていません。同じ文字・同じ短いコードを持つ別イベントも高得点になります。完全一致の確認は原本の認証情報・PNG全体との照合で別に行います。";
    public static string ShapeEvidence(int? readCode, int? candidateCode, TextSimilarity text)
    {
        var score = CorrespondenceScore.Calculate(text, readCode, candidateCode);
        string code = candidateCode is null ? "プレーン印影（コードなし）" : readCode is null ? "コード未読取（未確認）" : readCode == candidateCode ? "コード一致" : "コード不一致";
        string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        return $"履歴との対応スコア {F(score.Total)}（暫定） ／ {code}\n" +
            $"文字の形 {F(text.Contribution)} / 0.300［上段文字 {F(text.Name / 10)}・日付 {F(text.Date / 10)}・下段 {F(text.Bottom / 10)}］\n" +
            $"埋め込み情報 {F(score.CodeContribution)} / 0.700：{code}";
    }
    public static VerificationMessage Image(ImageSearchEvidence e)
    {
        if (e.Code is < 0 or > 4095 || e.CandidateCount < 0 || e.CodeMatchedCandidates < 0 || e.CodeHistoryCount < 0 || e.ComparedStampCount < 0 ||
            e.CodeMatchedCandidates > e.CandidateCount || e.CodeMatchedCandidates > e.CodeHistoryCount ||
            (e.Code is null && (e.CodeMatchedCandidates != 0 || e.CodeHistoryCount != 0)) ||
            (e.ComparedStampCount == 0 && e.CandidateCount > 0) ||
            (e.FilterEnabled && e.Code.HasValue && e.CandidateCount != e.CodeMatchedCandidates)) throw new ArgumentException("矛盾した照合結果です。");
        string read = e.Code.HasValue ? "印影コードを読み取れました。" : "印影コードは読み取れませんでした。";
        if (e.ComparedStampCount == 0)
            return new(VerificationMessageKind.Unavailable, "比較できる生成履歴がありません", read + " この保存先に照合可能な記録がないか、記録から比較用の印影を再現できませんでした。",
                "比較できていないため、対応する履歴の有無は判断していません。",
                "［履歴］で保存先と記録を確認してください。記録を追加した場合は［再照合］してください。");
        if (e.CodeMatchedCandidates > 0)
            return new(VerificationMessageKind.Candidate, "対応する生成履歴候補あり",
                $"印影コードが一致し、形も似ている生成履歴が{e.CodeMatchedCandidates}件見つかりました。" +
                (e.CandidateCount > e.CodeMatchedCandidates ? $" コードが異なる形のみの候補も{e.CandidateCount - e.CodeMatchedCandidates}件表示しています。" : ""),
                "履歴との対応を示す手がかりが得られました。元PNG・原本の完全一致ではないため、どの生成時の画像かは確定していません。",
                "上段文字・表示日付・下段文字を見比べ、候補を選んで［選択した生成履歴を開く］から生成日時や注釈を確認してください。");
        if (e.CandidateCount > 0)
            return new(VerificationMessageKind.Candidate, "形が似た履歴候補あり",
                read + $" 形が似ている生成履歴が{e.CandidateCount}件見つかりました。" + (e.Code.HasValue ? " 表示中の候補は、読み取ったコードとは一致していません。" : ""),
                "形だけを手がかりにした候補です。印影コードの一致や生成元を確認できた結果ではありません。",
                "文字と日付を確認してください。可能なら元PNGを選ぶか、印影を大きく含む画像で再照合してください。");
        if (e.Code is null)
            return new(VerificationMessageKind.Unavailable, "照合の手がかりが不足しています",
                "印影コードを読み取れず、今回の検索範囲では形が似た履歴も見つかりませんでした。",
                "読み取り失敗と、履歴がない場合を区別できません。偽造という意味ではありません。",
                "印影の円全体を囲み直すか、縮小・圧縮前の画像を選んでください。［履歴］の保存先も確認してください。");
        return new(VerificationMessageKind.NoCandidate, "今回の検索では履歴候補なし",
            read + (e.CodeHistoryCount > 0
                ? $" 同じコードの記録は{e.CodeHistoryCount}件ありますが、今回比較できた印影では形の候補基準を満たしませんでした。比較対象外の記録が含まれる場合もあります。"
                : " この保存先の照合可能な生成記録に、同じコードはありませんでした。"),
            "この検索で候補が見つからなかった結果です。未記録・別の保存先・画像の変化や誤読などを区別できず、偽造とは判断できません。",
            "［履歴］の保存先と、選択した印影の範囲を確認してください。必要に応じてコードの絞り込みを外し、形のみの候補も探せます。");
    }
    public static VerificationMessage Exact(VerificationResult result, bool original)
    {
        if (result.Status == VerificationStatus.Match && result.Matches.Count > 0)
            return new(VerificationMessageKind.Exact, "一致する生成履歴あり",
                original ? "原本の認証情報・記録ID・内容が、この保存先の生成記録と一致しました。"
                    : $"PNGファイル全体が生成時の記録と一致しました。一致した生成履歴は{result.Matches.Count}件です。",
                !original && result.Matches.Count > 1 ? "同じPNGに対応する記録が複数あるため、どの生成時の画像かはPNGだけでは選べません。" : "この保存先の生成記録との一致を確認できました。利用の許可や貼付完了を証明するものではありません。",
                "一覧から記録を選び、生成日時と注釈を確認してください。");
        if (result.Status == VerificationStatus.NoRecord)
            return new(VerificationMessageKind.NoCandidate, "この保存先に一致する生成履歴はありません", result.Explanation,
                "選択中の保存先での結果です。偽造という意味ではありません。", "生成時に使った履歴の保存先を確認してください。");
        return Unavailable(result.Explanation, "読み込み元と履歴の保存先を確認してください。検証できない記録を候補として扱うことはできません。");
    }
    public static VerificationMessage Unavailable(string reason, string nextStep) =>
        new(VerificationMessageKind.Unavailable, "照合を完了できませんでした", reason,
            "履歴との一致・不一致は判断していません。", nextStep);
}
