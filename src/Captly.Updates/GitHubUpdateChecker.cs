using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Captly.Core.Shell;

namespace Captly.Updates;

/// <summary>A release version: major.minor.patch, with an optional pre-release tag. A leading "v" is ignored.</summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static SemanticVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];
        var dash = value.IndexOf('-');
        var pre = dash >= 0 ? value[(dash + 1)..] : null;
        var parts = (dash >= 0 ? value[..dash] : value).Split('.');
        if (parts.Length is < 1 or > 3) return null;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return null;
        }
        return new SemanticVersion(numbers[0], numbers[1], numbers[2], string.IsNullOrEmpty(pre) ? null : pre);
    }

    /// <summary>A pre-release sorts below its release, as in SemVer 2.0.</summary>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;
        if (PreRelease == other.PreRelease) return 0;
        if (PreRelease == null) return 1;
        if (other.PreRelease == null) return -1;
        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public override string ToString() => PreRelease == null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}

/// <summary>
/// Asks GitHub for the repository's latest release, and compares it with this build (PLAN.md
/// Phase 8 step 6). It only reports; it never downloads or installs anything. A repository with no
/// releases yet answers 404, which counts as up to date.
/// </summary>
public sealed class GitHubUpdateChecker(HttpClient http, string repository, string currentVersion) : IUpdateChecker
{
    public const string Repository = "imahaider/Captly";

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Captly", currentVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheck(UpdateOutcome.UpToDate);
        if (!response.IsSuccessStatusCode) return new UpdateCheck(UpdateOutcome.Failed);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

        var latest = SemanticVersion.Parse(tag);
        var current = SemanticVersion.Parse(currentVersion);
        if (latest == null || current == null) return new UpdateCheck(UpdateOutcome.Failed);
        return latest.CompareTo(current) > 0
            ? new UpdateCheck(UpdateOutcome.Available, latest.ToString(), url)
            : new UpdateCheck(UpdateOutcome.UpToDate, latest.ToString(), url);
    }
}
