using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;

namespace Captly.Core.Tests;

internal sealed class FakeShell : IShellServices
{
    public List<string> Opened { get; } = [];
    public List<string?> SettingsPages { get; } = [];
    public string? FolderToPick { get; set; }
    public bool QuitCalled { get; private set; }
    public int SetupOpened { get; private set; }

    public void OpenSetup() => SetupOpened++;

    public void OpenSettings(string? page = null) => SettingsPages.Add(page);
    public void Open(string target) => Opened.Add(target);
    public string? PickFolder(string current) => FolderToPick;
    public void Quit() => QuitCalled = true;
}

internal sealed class FakeCaptioning(AppState state) : ICaptioning
{
    public int NextSourceCalls { get; private set; }

    public void SetOn(bool on) => state.IsOn = on;
    public void SetHidden(bool hidden) => state.IsHidden = hidden;
    public void NextSource() => NextSourceCalls++;
}

internal sealed class FakeModels : IModelManager
{
    public const string Tiny = "moonshine-tiny-streaming";
    public const string Small = "moonshine-small-streaming";
    public const string Medium = "moonshine-medium-streaming";

    private readonly HashSet<string> _installed;

    public FakeModels(params string[] installed) => _installed = installed.Length == 0 ? [Tiny] : [.. installed.Where(i => i.Length > 0)];

    /// <summary>No model installed, as on a fresh PC.</summary>
    public static FakeModels Empty() => new("");

    public Exception? DownloadError { get; set; }

    /// <summary>Downloads of these models wait until their task is completed.</summary>
    public Dictionary<string, TaskCompletionSource> Held { get; } = [];

    public List<string> Downloads { get; } = [];

    public IReadOnlyList<ModelInfo> Available =>
    [
        new(Tiny, "Moonshine Tiny Streaming", 34, 0, ModelArchitecture.TinyStreaming, _installed.Contains(Tiny)),
        new(Small, "Moonshine Small Streaming", 123, 0, ModelArchitecture.SmallStreaming, _installed.Contains(Small)),
        new(Medium, "Moonshine Medium Streaming", 245, 0, ModelArchitecture.MediumStreaming, _installed.Contains(Medium)),
    ];

    public string GetModelPath(string id) => id;

    public async Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
    {
        Downloads.Add(id);
        if (DownloadError != null) throw DownloadError;
        if (Held.TryGetValue(id, out var gate)) await gate.Task.WaitAsync(ct);
        progress?.Report(1);
        _installed.Add(id);
    }
}

internal static class Services
{
    /// <summary>Saves straight away into the list, so tests can see every write.</summary>
    public static SettingsService Settings(List<AppSettings>? saved = null, AppSettings? initial = null) =>
        new(initial ?? new AppSettings(), s => saved?.Add(s), TimeSpan.Zero);
}
