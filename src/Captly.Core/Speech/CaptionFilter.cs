using System.Globalization;

namespace Captly.Core.Speech;

/// <summary>
/// Removes text a speech model invents from noise. It knows nothing of timing, so it stays pure.
/// </summary>
public sealed class CaptionFilter
{
    /// <summary>Phrases models produce from silence or noise. Matched whole, ignoring case and punctuation.</summary>
    public static readonly IReadOnlyList<string> DefaultJunk =
    [
        "thank you for watching",
        "thanks for watching",
        "thank you for listening",
        "please subscribe",
        "see you next time",
        "you",
        "bye",
    ];

    private readonly HashSet<string> _junk;
    private string _lastFinal = "";

    public CaptionFilter(IEnumerable<string>? junk = null) =>
        _junk = (junk ?? DefaultJunk).Select(Normalize).ToHashSet();

    /// <summary>True if a partial is worth showing. Junk is not hidden here, as it may grow into real speech.</summary>
    public static bool HasWords(string text) => text.Any(char.IsLetterOrDigit);

    /// <summary>True if a finished line should be kept. An exact repeat of the last kept line is dropped.</summary>
    public bool AcceptFinal(string text)
    {
        if (!HasWords(text)) return false;
        var normalized = Normalize(text);
        if (_junk.Contains(normalized) || normalized == _lastFinal) return false;
        _lastFinal = normalized;
        return true;
    }

    public void Reset() => _lastFinal = "";

    private static string Normalize(string text)
    {
        var kept = text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(' ', new string(kept).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLower(CultureInfo.InvariantCulture);
    }
}
