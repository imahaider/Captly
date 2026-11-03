using Captly.Core.Speech;

namespace Captly.Core.Shell;

public enum SetupStep { Welcome, Model, Done }

/// <summary>One model card on the setup's second step.</summary>
public sealed class SetupChoice(ModelInfo model, Action<SetupChoice> choose) : Observable
{
    private bool _isSelected;
    private bool _isRecommended;

    public ModelInfo Model { get; private set; } = model;

    /// <summary>"Moonshine Small", as the design names it.</summary>
    public string Title => ModelRowModel.FullName(Model).Replace(" Streaming", "", StringComparison.OrdinalIgnoreCase);

    // D22: no promise of a graphics card.
    public string Detail => Model.Architecture switch
    {
        ModelArchitecture.TinyStreaming => $"Fastest · works on any PC · {Model.MillionParameters}M parameters",
        ModelArchitecture.SmallStreaming => $"More accurate · needs a faster processor · {Model.MillionParameters}M parameters",
        _ => $"Most accurate · needs a fast processor · {Model.MillionParameters}M parameters",
    };

    public bool IsRecommended { get => _isRecommended; set => Set(ref _isRecommended, value); }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected) choose(this);
        }
    }

    internal void ShowSelected(bool selected) => Set(ref _isSelected, selected, nameof(IsSelected));

    internal void Refresh(ModelInfo model) => Model = model;
}

/// <summary>
/// First-run setup (PLAN.md §6.3 #11–13, Phase 9). Step 2 cannot test this PC without a model, so
/// it fetches Tiny first, which every PC can run and which is the fallback anyway. Then it runs the
/// 10 s speed test, recommends a model (D40), preselects it, and fetches it too if it is not Tiny.
/// Continue waits until the chosen model is downloaded and verified.
/// </summary>
public sealed class SetupModel : Observable
{
    private readonly SettingsService _settings;
    private readonly IModelManager _models;
    private readonly Func<ModelInfo, CancellationToken, Task<BenchResult>> _speedTest;
    private readonly CancellationTokenSource _cancel = new();
    private SetupStep _step;
    private SetupChoice? _first;
    private SetupChoice? _second;
    private string _modelSubtitle = "Captly is checking your PC…";
    private string? _progressText;
    private double _progress;
    private bool _busy;
    private bool _checked;
    private string? _error;
    private bool _startWithWindows = true;
    private CancellationTokenSource? _download;

    public SetupModel(SettingsService settings, IModelManager models, Func<ModelInfo, CancellationToken, Task<BenchResult>> speedTest)
    {
        _settings = settings;
        _models = models;
        _speedTest = speedTest;
        ContinueCommand = new Command(Continue, () => CanContinue);
        BackCommand = new Command(() => Step = Step - 1, () => Step > SetupStep.Welcome);
        SkipCommand = new Command(Skip);
        StartCommand = new Command(Finish);
        RetryCommand = new AsyncCommand(RetryAsync);
    }

    /// <summary>Raised once: true when setup was finished, false when skipped or closed.</summary>
    public event Action<bool>? Finished;

    public SetupStep Step
    {
        get => _step;
        private set
        {
            if (!Set(ref _step, value)) return;
            Raise(nameof(StepIndex));
            RefreshCommands();
            if (value == SetupStep.Model && !_checked) _ = PrepareModelAsync();
        }
    }

    public int StepIndex => (int)Step;

    public SetupChoice? First { get => _first; private set => Set(ref _first, value); }

    public SetupChoice? Second { get => _second; private set => Set(ref _second, value); }

    public string ModelSubtitle { get => _modelSubtitle; private set => Set(ref _modelSubtitle, value); }

    /// <summary>"Downloading Moonshine Small…", or what Captly is doing now.</summary>
    public string? ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    public double Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value)) Raise(nameof(ProgressPercent));
        }
    }

    public string ProgressPercent => $"{Progress * 100:0}%";

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) RefreshCommands();
        }
    }

    /// <summary>A failed download, in plain words. Retry and Skip Setup stay available.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value)) Raise(nameof(HasError));
        }
    }

    public bool HasError => Error != null;

    public string Note => "Only needed once. After this, Captly works offline.";

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    public SetupChoice? Selected => First?.IsSelected == true ? First : Second?.IsSelected == true ? Second : null;

    public bool CanContinue => Step switch
    {
        SetupStep.Welcome => true,
        SetupStep.Model => !IsBusy && Selected is { Model.IsInstalled: true },
        _ => false,
    };

    public Command ContinueCommand { get; }

    public Command BackCommand { get; }

    public Command SkipCommand { get; }

    public Command StartCommand { get; }

    public AsyncCommand RetryCommand { get; }

    /// <summary>Setup is needed until it is finished once, or while no model is installed.</summary>
    public static bool IsNeeded(bool setupCompleted, IModelManager models) => !setupCompleted || !models.Available.Any(m => m.IsInstalled);

    private void Continue()
    {
        if (CanContinue) Step++;
    }

    // Once the PC is tested, only the chosen model's download needs another try.
    private Task RetryAsync()
    {
        if (!_checked || Selected is not { } selected) return PrepareModelAsync();
        Error = null;
        return DownloadChoiceAsync(selected);
    }

    /// <summary>Step 2's work: fetch Tiny, test this PC, recommend, and fetch the recommended model.</summary>
    private async Task PrepareModelAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            var tiny = _models.Available.MinBy(m => m.MillionParameters)!;
            if (!tiny.IsInstalled && !await DownloadAsync(tiny)) return;

            ModelSubtitle = "Captly is checking your PC…";
            ProgressText = "Checking how fast captions run on this PC…";
            Progress = 0;
            BenchResult? bench = null;
            try
            {
                bench = await _speedTest(Installed(tiny.Id), _cancel.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Without a result, Tiny is the safe choice.
            }

            var recommended = ModelRecommender.Recommend(_models.Available, bench).ModelId;
            var best = _models.Available.First(m => m.Id == recommended);
            var other = best.Id == tiny.Id ? _models.Available.Where(m => m.Id != tiny.Id).MinBy(m => m.MillionParameters) : tiny;
            First = new SetupChoice(best, Choose) { IsRecommended = true };
            Second = other == null ? null : new SetupChoice(other, Choose);
            ModelSubtitle = "Captly checked your PC. This one fits best — you can change it later.";
            _checked = true;
            IsBusy = false;
            Choose(First);
        }
        catch (OperationCanceledException)
        {
            // Skipped.
        }
        finally
        {
            IsBusy = _download != null;
        }
    }

    private void Choose(SetupChoice choice)
    {
        First?.ShowSelected(choice == First);
        Second?.ShowSelected(choice == Second);
        Raise(nameof(Selected));

        // Picking another model stops a download in progress. It resumes if picked again.
        _download?.Cancel();
        if (!choice.Model.IsInstalled) _ = DownloadChoiceAsync(choice);
        else ShowReady(choice);
        RefreshCommands();
    }

    private async Task DownloadChoiceAsync(SetupChoice choice)
    {
        IsBusy = true;
        if (await DownloadAsync(choice.Model))
        {
            choice.Refresh(Installed(choice.Model.Id));
            if (Selected == choice) ShowReady(choice);
        }
        IsBusy = _download != null;
        RefreshCommands();
    }

    private void ShowReady(SetupChoice choice)
    {
        ProgressText = $"{choice.Title} is ready.";
        Progress = 1;
    }

    private async Task<bool> DownloadAsync(ModelInfo model)
    {
        var name = ModelRowModel.FullName(model).Replace(" Streaming", "", StringComparison.OrdinalIgnoreCase);
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        _download = download;
        ProgressText = $"Downloading {name}…";
        Progress = 0;
        Error = null;
        try
        {
            await _models.DownloadAsync(model.Id, new Progress<double>(p =>
            {
                if (_download == download) Progress = p;
            }), download.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            Error = $"Couldn't download {name}. Check your internet connection, then try again.";
            ProgressText = null;
            return false;
        }
        finally
        {
            if (_download == download) _download = null;
        }
    }

    private ModelInfo Installed(string id) => _models.Available.First(m => m.Id == id);

    private void Skip()
    {
        _cancel.Cancel();
        // With a model already here there is nothing left to finish, so setup does not come back.
        if (_models.Available.Any(m => m.IsInstalled)) _settings.Update(s => s with { SetupCompleted = true });
        Finished?.Invoke(false);
    }

    private void Finish()
    {
        var model = Selected?.Model is { IsInstalled: true } chosen ? chosen.Id : _settings.Current.Engine.Model;
        _settings.Update(s => s with
        {
            SetupCompleted = true,
            Engine = s.Engine with { Model = model },
            General = s.General with { StartWithWindows = StartWithWindows },
        });
        Finished?.Invoke(true);
    }

    private void RefreshCommands()
    {
        ContinueCommand.Refresh();
        BackCommand.Refresh();
        Raise(nameof(CanContinue));
    }
}
