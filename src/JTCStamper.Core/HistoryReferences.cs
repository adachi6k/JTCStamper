namespace JTCStamper.Core;

public static class HistoryReferences
{
    // Input is authenticated history in append order. Preserve every distinct image of a stamp.
    public static Generation[] LatestImages(IEnumerable<Generation> history, int limit = 300) =>
        history.Reverse().DistinctBy(g => (g.Stamp, g.PngSha256.ToUpperInvariant())).Take(limit).ToArray();

    public static IEnumerable<Generation> Matching(IEnumerable<Generation> history, VisualCandidate candidate) =>
        history.Reverse().Where(g => g.Stamp == candidate.Stamp &&
            g.PngSha256.Equals(candidate.PngSha256, StringComparison.OrdinalIgnoreCase));
}
