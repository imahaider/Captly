using Captly.Core.Settings;

namespace Captly.Core.Audio;

/// <summary>What Captly listens to. A null PID means all system audio.</summary>
public sealed record CaptureTarget(int? RootPid, string ExeName, string DisplayName)
{
    public static CaptureTarget SystemAudio { get; } = new(null, "", "All system audio");

    public bool IsSystem => RootPid is null;

    public static CaptureTarget For(AudioAppInfo app) => new(app.RootPid, app.ExeName, app.DisplayName);

    public bool IsSameAs(CaptureTarget? other) => other != null && other.RootPid == RootPid;
}

public sealed record SelectorInput(
    IReadOnlyList<AudioAppInfo> Apps,
    SourceSettings Settings,
    bool SupportsPerProcess,
    Func<int, bool> IsRunning,
    int? MediaOwnerPid = null);

/// <summary>
/// Picks the capture target from the apps making sound, following PLAN.md Phase 5. Pure and
/// clock-driven, so every rule is unit-tested.
/// <list type="bullet">
/// <item>All system audio when chosen, or when Windows cannot capture one app.</item>
/// <item>Pinned: that app whenever it is present, otherwise the Auto rules.</item>
/// <item>Auto: the app that owns the playing media, else the loudest. Switching away from a
/// running target waits for the switch delay, so a short notification never steals captions.
/// A quiet target gives way to the media owner at once, because that is a real change of media.</item>
/// </list>
/// A target that goes quiet is kept while it runs, so pausing a video does not lose it.
/// </summary>
public sealed class SourceSelector(TimeProvider? clock = null)
{
    /// <summary>Peaks below this count as silence.</summary>
    public const float ActivePeak = 0.01f;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private CaptureTarget? _pending;
    private DateTimeOffset _pendingSince;

    public CaptureTarget? Current { get; private set; }

    /// <summary>Returns true when <see cref="Current"/> changed.</summary>
    public bool Update(SelectorInput input)
    {
        var next = Choose(input);
        var changed = next is null ? Current is not null : !next.IsSameAs(Current);
        Current = next;
        return changed;
    }

    /// <summary>Drops the current target, so the next update chooses afresh. Used when capture fails to start.</summary>
    public void Forget()
    {
        Current = null;
        _pending = null;
    }

    private CaptureTarget? Choose(SelectorInput input)
    {
        if (!input.SupportsPerProcess || input.Settings.Mode == SourceMode.System)
        {
            _pending = null;
            return CaptureTarget.SystemAudio;
        }

        var current = Current is { IsSystem: false } target && (IsPresent(target, input) || input.IsRunning(target.RootPid!.Value)) ? target : null;

        if (input.Settings.Mode == SourceMode.Pinned && !string.IsNullOrWhiteSpace(input.Settings.PinnedExe))
        {
            var pinned = input.Apps.Where(a => SameExe(a.ExeName, input.Settings.PinnedExe)).MaxBy(a => a.PeakLevel);
            if (pinned != null)
            {
                _pending = null;
                return current != null && current.RootPid == pinned.RootPid ? current : CaptureTarget.For(pinned);
            }
            if (current != null && SameExe(current.ExeName, input.Settings.PinnedExe)) return current;
        }

        var best = Best(input);
        if (current == null)
        {
            _pending = null;
            return best == null ? null : CaptureTarget.For(best);
        }

        if (best == null || best.RootPid == current.RootPid)
        {
            _pending = null;
            return current;
        }

        var currentApp = input.Apps.FirstOrDefault(a => a.RootPid == current.RootPid);
        if (best.RootPid == input.MediaOwnerPid && (currentApp?.PeakLevel ?? 0) < ActivePeak)
        {
            _pending = null;
            return CaptureTarget.For(best);
        }

        var now = _clock.GetUtcNow();
        if (_pending?.RootPid != best.RootPid)
        {
            _pending = CaptureTarget.For(best);
            _pendingSince = now;
        }
        if (now - _pendingSince < TimeSpan.FromMilliseconds(input.Settings.SwitchDelayMs)) return current;

        _pending = null;
        return CaptureTarget.For(best);
    }

    private static AudioAppInfo? Best(SelectorInput input)
    {
        var owner = input.MediaOwnerPid is { } pid ? input.Apps.FirstOrDefault(a => a.RootPid == pid) : null;
        return owner ?? input.Apps.Where(a => a.PeakLevel >= ActivePeak).MaxBy(a => a.PeakLevel);
    }

    private static bool IsPresent(CaptureTarget target, SelectorInput input) => input.Apps.Any(a => a.RootPid == target.RootPid);

    /// <summary>Compares exe names, with or without ".exe", ignoring case.</summary>
    public static bool SameExe(string a, string? b)
    {
        if (b is null) return false;
        static string Trim(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
    }
}
