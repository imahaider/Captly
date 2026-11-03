using Captly.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Captly.Core.Audio;

/// <summary>
/// Polls the apps making sound and points the switching source at whatever
/// <see cref="SourceSelector"/> picks. Twice a second is enough to follow apps. While level
/// meters are on screen, <see cref="ShowingLevels"/> raises it to ten.
/// </summary>
public sealed class SourceController(
    IAudioSourceFactory factory,
    SwitchingAudioSource output,
    Func<SourceSettings> settings,
    ILogger<SourceController> logger,
    TimeProvider? clock = null,
    Func<IReadOnlyList<AudioAppInfo>, int?>? mediaOwner = null) : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan LevelPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly SourceSelector _selector = new(clock);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public CaptureTarget? Target => _selector.Current;

    /// <summary>Set while the Source screen or popover shows level meters.</summary>
    public bool ShowingLevels { get; set; }

    /// <summary>The latest list, loudest first.</summary>
    public IReadOnlyList<AudioAppInfo> Apps { get; private set; } = [];

    /// <summary>Raised on the polling thread after each poll.</summary>
    public event Action<IReadOnlyList<AudioAppInfo>>? AppsUpdated;

    /// <summary>Raised on the polling thread once the new source is running. Null means nothing is captured.</summary>
    public event Action<CaptureTarget?>? TargetChanged;

    public void Start() => _loop = Task.Run(Loop);

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop logs its own failures.
        }
        _stop.Dispose();
    }

    private async Task Loop()
    {
        try
        {
            while (true)
            {
                try
                {
                    Poll();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Source poll failed");
                }
                await Task.Delay(ShowingLevels ? LevelPollInterval : PollInterval, _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    internal void Poll()
    {
        var perProcess = factory.SupportsPerProcess;
        Apps = perProcess ? factory.ListActiveApps() : [];
        AppsUpdated?.Invoke(Apps);

        var input = new SelectorInput(Apps, settings(), perProcess, factory.IsRunning, mediaOwner?.Invoke(Apps));
        if (!_selector.Update(input)) return;

        var target = _selector.Current;
        try
        {
            output.SwitchTo(target switch
            {
                null => null,
                { IsSystem: true } => factory.CreateSystemWide(),
                _ => factory.CreateForProcess(target.RootPid!.Value),
            });
            logger.LogInformation("Caption source is now {Target}", target?.DisplayName ?? "nothing");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not start capturing {Target}", target?.DisplayName);
            _selector.Forget();
            output.SwitchTo(null);
            target = null;
        }
        TargetChanged?.Invoke(target);
    }
}
