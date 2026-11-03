namespace Captly.Bench;

internal static class Wer
{
    /// <summary>Word error rate: edits needed, as a fraction of the reference length.</summary>
    public static double Compute(string reference, string hypothesis)
    {
        var r = Words(reference);
        var h = Words(hypothesis);
        var row = Enumerable.Range(0, h.Length + 1).ToArray();
        for (var i = 1; i <= r.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= h.Length; j++)
            {
                var cost = r[i - 1] == h[j - 1] ? previous : previous + 1;
                previous = row[j];
                row[j] = Math.Min(cost, Math.Min(row[j], row[j - 1]) + 1);
            }
        }
        return row[h.Length] / (double)r.Length;
    }

    private static string[] Words(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '\'' ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
