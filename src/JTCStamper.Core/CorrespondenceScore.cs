namespace JTCStamper.Core;

// Provisional evidence weights, not a probability or authentication decision.
public sealed record CorrespondenceScore(TextSimilarity Text, double CodeContribution)
{
    public double Total => Text.Contribution + CodeContribution;
    public static CorrespondenceScore Calculate(TextSimilarity text, int? readCode, int? candidateCode)
    {
        foreach (double value in new[] { text.Name, text.Date, text.Bottom })
            if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(text));
        if (readCode is < 0 or > 4095 || candidateCode is < 0 or > 4095) throw new ArgumentOutOfRangeException(nameof(readCode));
        return new(text, readCode.HasValue && readCode == candidateCode ? 0.7 : 0);
    }
}
