namespace Captly.Core.Shell;

public enum UpdateOutcome { UpToDate, Available, Failed }

/// <summary>The newest release, as the update check found it.</summary>
public sealed record UpdateCheck(UpdateOutcome Outcome, string? LatestVersion = null, string? Url = null);

/// <summary>Looks for a newer release. It never downloads or installs anything (PLAN.md Phase 8).</summary>
public interface IUpdateChecker
{
    Task<UpdateCheck> CheckAsync(CancellationToken ct);
}

/// <summary>Shared by the General and About screens, so a check from either shows on both.</summary>
public sealed class UpdateStatus : Observable
{
    private readonly IUpdateChecker _checker;
    private string _text;
    private UpdateCheck? _last;

    /// <param name="open">Opens the release page in the browser.</param>
    public UpdateStatus(IUpdateChecker checker, string version, Action<string> open)
    {
        _checker = checker;
        Version = version;
        _text = "Not checked yet.";
        CheckCommand = new AsyncCommand(CheckAsync);
        OpenReleaseCommand = new Command(() =>
        {
            if (Last?.Url is { } url) open(url);
        }, () => IsAvailable);
    }

    public string Version { get; }

    /// <summary>Such as "You're up to date."</summary>
    public string Text { get => _text; private set => Set(ref _text, value); }

    public UpdateCheck? Last { get => _last; private set => Set(ref _last, value); }

    public bool IsAvailable => Last?.Outcome == UpdateOutcome.Available;

    public AsyncCommand CheckCommand { get; }

    public Command OpenReleaseCommand { get; }

    public async Task CheckAsync()
    {
        Text = "Checking for updates…";
        UpdateCheck result;
        try
        {
            result = await _checker.CheckAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            result = new UpdateCheck(UpdateOutcome.Failed);
        }

        Last = result;
        Raise(nameof(IsAvailable));
        OpenReleaseCommand.Refresh();
        Text = result.Outcome switch
        {
            UpdateOutcome.UpToDate => "You're up to date.",
            UpdateOutcome.Available => $"Captly {result.LatestVersion} is available.",
            _ => "Couldn't check for updates. Try again later.",
        };
    }
}
