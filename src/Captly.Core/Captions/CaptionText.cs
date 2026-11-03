using System.Text;
using System.Text.RegularExpressions;

namespace Captly.Core.Captions;

/// <summary>Cleans caption text and breaks it into lines. Pure, so it is easy to test.</summary>
public static partial class CaptionText
{
    /// <summary>Collapses whitespace and doubled commas, and capitalises sentence starts.</summary>
    public static string Normalize(string text)
    {
        var collapsed = Whitespace().Replace(text, " ").Trim();
        collapsed = DoubledCommas().Replace(collapsed, ",");

        var result = new StringBuilder(collapsed);
        var sentenceStart = true;
        for (var i = 0; i < result.Length; i++)
        {
            var c = result[i];
            if (char.IsLetter(c) && sentenceStart) result[i] = char.ToUpperInvariant(c);
            if (!char.IsWhiteSpace(c)) sentenceStart = c is '.' or '?' or '!';
        }
        return result.ToString();
    }

    /// <summary>
    /// Greedily fills lines up to the width, breaking only between words. A word longer than a
    /// whole line keeps its own line, because cutting a word is worse than a long line.
    /// </summary>
    public static List<string> Wrap(string text, int charsPerLine)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > charsPerLine)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(",{2,}")]
    private static partial Regex DoubledCommas();
}
