using System.Globalization;

namespace JTCStamper.Core;

public enum VerificationMessageKind { Exact, Candidate, NoCandidate, Unavailable }
public sealed record VerificationMessage(VerificationMessageKind Kind, string Title, string Findings, string Meaning, string NextStep);
public sealed record ImageSearchEvidence(int? Code, int CandidateCount, int CodeMatchedCandidates,
    int CodeHistoryCount, int ComparedStampCount, bool FilterEnabled);

// Presentation only: a visual candidate must never be promoted to an exact/authenticated match.
public static class VerificationMessages
{
    public const string ScoreHelp = "形の近さは確率ではありません。1.000でも、元のPNGファイルとの完全一致を意味しません。";
    public const string MethodHelp = "形の近さは、印影を96×96にそろえ、円の内側の文字と線を少し回転させて比較した参考値です。上下左右・斜めに1画素のずれを許容し、小数点以下3桁に丸めて表示します。文字を読み取って氏名や日付の意味を確認する処理ではありません。\n印影コードは履歴を探すための短い値です。同じコードが別の生成記録に現れることがあり、コード一致だけでは生成元を特定できません。";
    public static string ShapeEvidence(int? readCode, int candidateCode, double score)
    {
        if (!double.IsFinite(score) || score < 0 || score > 1) throw new ArgumentOutOfRangeException(nameof(score));
        string code = readCode is null ? "コード未読取・形のみの候補" : readCode == candidateCode ? "コード一致" : "コード不一致・形のみの候補";
        return code + " ／ 形の近さ " + score.ToString("0.000", CultureInfo.InvariantCulture) + "（参考値）";
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
                "氏名・表示日付・下段文字を見比べ、候補を選んで［選択した生成履歴を開く］から生成日時や注釈を確認してください。");
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
