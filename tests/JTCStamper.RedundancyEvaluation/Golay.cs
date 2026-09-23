using System.Numerics;

// Experimental extended binary Golay [24,12,8]. No authentication claim.
static class Golay
{
    public static uint Encode(int data)
    {
        if (data is < 0 or > 4095) throw new ArgumentOutOfRangeException(nameof(data));
        uint shifted = (uint)data << 11, remainder = shifted;
        for (int bit = 22; bit >= 11; bit--)
            if ((remainder & (1u << bit)) != 0) remainder ^= 0xAE3u << (bit - 11);
        uint word = shifted | remainder;
        return (word << 1) | (uint)(BitOperations.PopCount(word) & 1);
    }
    static readonly Dictionary<uint, uint> Errors = Build();
    static uint Syndrome(uint word) => (word ^ Encode((int)(word >> 12))) & 4095;
    static Dictionary<uint, uint> Build()
    {
        var result = new Dictionary<uint, uint>();
        foreach (uint error in ErrorMasks()) result.Add(Syndrome(error), error);
        return result;
    }
    public static IEnumerable<uint> ErrorMasks()
    {
        yield return 0;
        for (int a = 0; a < 24; a++)
        {
            yield return 1u << a;
            for (int b = a + 1; b < 24; b++)
            {
                yield return (1u << a) | (1u << b);
                for (int c = b + 1; c < 24; c++) yield return (1u << a) | (1u << b) | (1u << c);
            }
        }
    }
    public static (int Code, int Corrections)? Decode(uint word)
    {
        if (word >= 1u << 24) throw new ArgumentOutOfRangeException(nameof(word));
        return Errors.TryGetValue(Syndrome(word), out uint error)
            ? ((int)((word ^ error) >> 12), BitOperations.PopCount(error)) : null;
    }
    public static object Check()
    {
        int distance = Enumerable.Range(1, 4095).Min(x => BitOperations.PopCount(Encode(x)));
        if (distance != 8) throw new Exception("Minimum distance");
        long checkedCases = 0;
        var masks = ErrorMasks().ToArray();
        for (int code = 0; code < 4096; code++)
        foreach (uint mask in masks)
        {
            var decoded = Decode(Encode(code) ^ mask);
            if (decoded?.Code != code || decoded?.Corrections != BitOperations.PopCount(mask)) throw new Exception("Correction failed");
            checkedCases++;
        }
        int rejectedFour = 0;
        for (int a = 0; a < 24; a++) for (int b = a + 1; b < 24; b++)
        for (int c = b + 1; c < 24; c++) for (int d = c + 1; d < 24; d++)
        {
            if (Decode((1u << a) | (1u << b) | (1u << c) | (1u << d)) != null) throw new Exception("Four-bit acceptance");
            rejectedFour++;
        }
        return new { MinimumDistance = distance, CorrectedCases = checkedCases, RejectedFourBitMasksAtZero = rejectedFour,
            Note = "Linear code: four-bit rejection holds under translation by any codeword. More than four errors may miscorrect." };
    }
}
