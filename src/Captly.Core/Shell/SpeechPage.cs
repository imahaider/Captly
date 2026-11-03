using System.Collections.ObjectModel;
using Captly.Core.Settings;
using Captly.Core.Speech;

namespace Captly.Core.Shell;

public enum ModelRowState { Available, Downloading, Installed, InUse }

/// <summary>One model on the Speech Model screen.</summary>
public sealed class ModelRowModel(ModelInfo model) : Observable
{
    private ModelRowState _state;
    private double _progress;
    private bool _isRecommended;

    public ModelInfo Model => model;

    public string Id => model.Id;

    public string Name => FullName(model);

    /// <summary>The catalogue says "Tiny Streaming". The designs name the family too.</summary>
    public static string FullName(ModelInfo model) =>
        model.DisplayName.StartsWith("Moonshine", StringComparison.OrdinalIgnoreCase) ? model.DisplayName : "Moonshine " + model.DisplayName;

    public string Parameters => $"{model.MillionParameters}M parameters";

    // D22: nothing here promises a graphics card, because none is used yet.
    public string Description => model.Architecture switch
    {
        ModelArchitecture.TinyStreaming => "Fastest. Great for clear speech on any PC.",
        ModelArchitecture.SmallStreaming => "More accurate. Needs a faster processor.",
        _ => "Most accurate. Needs a fast processor.",
    };

    public ModelRowState State { get => _state; set => Set(ref _state, value); }

    /// <summary>From 0 to 1 while downloading.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public bool IsRecommended { get => _isRecommended; set => Set(ref _isRecommended, value); }
}

/// <summary>
/// The Speech Model screen (PLAN.md §6.3 #5). Models download with progress and switch live.
/// The graphics card controls stay but are unavailable, because no model can use one (D3, D22).
/// </summary>
public sealed class SpeechPageModel : SettingsPageModel
{
    private readonly IModelManager _models;
    private readonly Func<ModelInfo, CancellationToken, Task<BenchResult>> _speedTest;
    private readonly HashSet<string> _downloading = [];
    private BenchResult? _bench;
    private string? _testResult;
    private string? _message;

    public SpeechPageModel(SettingsService settings, IModelManager models, Func<ModelInfo, CancellationToken, Task<BenchResult>> speedTest, IReadOnlyList<GraphicsAdapter> adapters)
        : base(settings)
    {
        _models = models;
        _speedTest = speedTest;
        Rows = new ObservableCollection<ModelRowModel>(models.Available.Select(m => new ModelRowModel(m)));
        Adapters = adapters.Count > 0 ? adapters.Select(a => new Choice<string>(a.Name, a.Id)).ToList() : [new Choice<string>("No graphics card found", "")];
        SpeedTestCommand = new AsyncCommand(RunSpeedTestAsync);
        settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<ModelRowModel> Rows { get; }

    /// <summary>True once a model that can use a graphics card is installed. None can yet.</summary>
    public bool CanUseGraphicsCard => false;

    /// <summary>The segments read Automatic, Graphics card, Processor.</summary>
    public int RunOnIndex
    {
        get => Current.Engine.Device switch { EngineDevice.Gpu => 1, EngineDevice.Cpu => 2, _ => 0 };
        set => Change(s => s with { Engine = s.Engine with { Device = value switch { 1 => EngineDevice.Gpu, 2 => EngineDevice.Cpu, _ => EngineDevice.Auto } } });
    }

    public string RunOnNote => "No speech model can use a graphics card yet, so captions run on the processor.";

    public IReadOnlyList<Choice<string>> Adapters { get; }

    public Choice<string> Adapter
    {
        get => Adapters.FirstOrDefault(a => a.Value == Current.Engine.AdapterId) ?? Adapters[0];
        set => Change(s => s with { Engine = s.Engine with { AdapterId = value.Value.Length == 0 ? null : value.Value } });
    }

    public AsyncCommand SpeedTestCommand { get; }

    public string TestDescription => _testResult ?? "Checks how fast captions run here and suggests the best model.";

    public IReadOnlyList<Choice<string>> Languages { get; } = [new("English", "en")];

    public Choice<string> Language
    {
        get => Languages.FirstOrDefault(l => l.Value == Current.Engine.Language) ?? Languages[0];
        set => Change(s => s with { Engine = s.Engine with { Language = value.Value } });
    }

    /// <summary>A problem to show, such as a failed download.</summary>
    public string? Message { get => _message; private set => Set(ref _message, value); }

    public async Task DownloadAsync(ModelRowModel row)
    {
        if (!_downloading.Add(row.Id)) return;
        Message = null;
        row.Progress = 0;
        Refresh();
        try
        {
            await _models.DownloadAsync(row.Id, new Progress<double>(p => row.Progress = p), CancellationToken.None);
        }
        catch (Exception)
        {
            Message = $"{row.Name} didn't download. Check your connection and try again.";
        }
        finally
        {
            _downloading.Remove(row.Id);
        }
        Refresh();
    }

    /// <summary>Switches captions to an installed model. The caption pipeline reloads it.</summary>
    public void Use(ModelRowModel row)
    {
        if (row.State == ModelRowState.Installed) Change(s => s with { Engine = s.Engine with { Model = row.Id } });
    }

    private async Task RunSpeedTestAsync()
    {
        var installed = _models.Available.Where(m => m.IsInstalled).ToList();
        var model = installed.FirstOrDefault(m => m.Id == Current.Engine.Model) ?? installed.MinBy(m => m.MillionParameters);
        if (model == null)
        {
            SetTestResult("Download a model first, then run the test.");
            return;
        }

        SetTestResult("Testing… this takes a few seconds.");
        try
        {
            _bench = await _speedTest(model, CancellationToken.None);
        }
        catch (Exception)
        {
            SetTestResult("The test didn't finish. Try again.");
            return;
        }

        var best = ModelRecommender.Recommend(_models.Available, _bench);
        var bestName = ModelRowModel.FullName(_models.Available.First(m => m.Id == best.ModelId));
        SetTestResult($"{ModelRowModel.FullName(model)} took {_bench.RealTimeFactor * 10:0.0} s to caption 10 s of speech. Best for this PC: {bestName}.");
        Refresh();
    }

    private void SetTestResult(string text)
    {
        _testResult = text;
        Raise(nameof(TestDescription));
    }

    private void Refresh()
    {
        var available = _models.Available;
        var recommended = ModelRecommender.Recommend(available, _bench).ModelId;
        foreach (var row in Rows)
        {
            var model = available.FirstOrDefault(m => m.Id == row.Id) ?? row.Model;
            row.State = _downloading.Contains(row.Id) ? ModelRowState.Downloading
                : !model.IsInstalled ? ModelRowState.Available
                : model.Id == Current.Engine.Model ? ModelRowState.InUse
                : ModelRowState.Installed;
            row.IsRecommended = row.Id == recommended;
        }
    }
}
