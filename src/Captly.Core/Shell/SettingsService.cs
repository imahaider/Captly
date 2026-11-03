using Captly.Core.Settings;

namespace Captly.Core.Shell;

/// <summary>
/// The settings in force. Every change applies at once through <see cref="Changed"/>, and is
/// written to disk after a short pause, so dragging a slider does not write the file 60 times a
/// second. Use it from the UI thread.
/// </summary>
public sealed class SettingsService : IDisposable
{
    public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly Action<AppSettings> _save;
    private readonly TimeSpan _saveDelay;
    private readonly Timer _timer;
    private readonly Lock _gate = new();
    private bool _dirty;

    /// <param name="saveDelay">Zero saves straight away, which tests use.</param>
    public SettingsService(AppSettings initial, Action<AppSettings> save, TimeSpan? saveDelay = null)
    {
        Current = initial;
        _save = save;
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        _timer = new Timer(_ => Flush());
    }

    public AppSettings Current { get; private set; }

    /// <summary>Raised with the old and new settings, after <see cref="Current"/> changed.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    /// <summary>Applies a change. Returning the same settings does nothing.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        var old = Current;
        var next = change(old);
        if (next == old) return;

        Current = next;
        lock (_gate) _dirty = true;
        Changed?.Invoke(old, next);

        if (_saveDelay == TimeSpan.Zero) Flush();
        else _timer.Change(_saveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes any unsaved change now.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            _save(Current);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
