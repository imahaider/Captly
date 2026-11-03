using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;

namespace Captly.Core.Tests;

public class SetupModelTests
{
    private readonly SettingsService _settings = Services.Settings();
    private readonly List<bool> _finished = [];

    private SetupModel Setup(FakeModels models, double tinyRtf = 0.31, bool testFails = false)
    {
        var setup = new SetupModel(_settings, models, (model, _) => testFails
            ? Task.FromException<BenchResult>(new InvalidOperationException())
            : Task.FromResult(new BenchResult(model.Id, model.MillionParameters, tinyRtf)));
        setup.Finished += _finished.Add;
        return setup;
    }

    // Progress<T> reports on the thread pool, and the steps continue after awaits.
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task SlowPc_FetchesTiny_TestsIt_AndRecommendsIt()
    {
        var models = FakeModels.Empty();
        var setup = Setup(models);
        Assert.True(setup.CanContinue);

        setup.ContinueCommand.Execute(null);
        await Until(() => setup.CanContinue);

        Assert.Equal(SetupStep.Model, setup.Step);
        Assert.Equal([FakeModels.Tiny], models.Downloads);
        Assert.Equal(FakeModels.Tiny, setup.First!.Model.Id);
        Assert.True(setup.First.IsRecommended);
        Assert.True(setup.First.IsSelected);
        Assert.Equal(FakeModels.Small, setup.Second!.Model.Id);
        Assert.False(setup.Second.IsRecommended);
        Assert.StartsWith("Captly checked your PC", setup.ModelSubtitle);
        Assert.Equal("Moonshine Tiny", setup.First.Title);
        Assert.DoesNotContain("graphics", setup.Second.Detail);
    }

    [Fact]
    public async Task FastPc_RecommendsSmall_AndFetchesIt_BeforeContinue()
    {
        var models = FakeModels.Empty();
        models.Held[FakeModels.Small] = new TaskCompletionSource();
        var setup = Setup(models, tinyRtf: 0.2);

        setup.ContinueCommand.Execute(null);
        await Until(() => setup.First != null);

        Assert.Equal(FakeModels.Small, setup.First!.Model.Id);
        Assert.Equal(FakeModels.Tiny, setup.Second!.Model.Id);
        Assert.True(setup.IsBusy);
        Assert.False(setup.CanContinue);
        Assert.Equal("Downloading Moonshine Small…", setup.ProgressText);

        models.Held[FakeModels.Small].SetResult();
        await Until(() => setup.CanContinue);
        Assert.Equal("Moonshine Small is ready.", setup.ProgressText);
    }

    [Fact]
    public async Task PickingTiny_StopsTheSmallDownload()
    {
        var models = FakeModels.Empty();
        models.Held[FakeModels.Small] = new TaskCompletionSource();
        var setup = Setup(models, tinyRtf: 0.2);
        setup.ContinueCommand.Execute(null);
        await Until(() => setup.First != null);

        setup.Second!.IsSelected = true;

        await Until(() => setup.CanContinue);
        Assert.False(setup.First!.IsSelected);
        Assert.False(setup.IsBusy);
    }

    [Fact]
    public async Task Offline_ShowsAnError_AndTryAgainRecovers()
    {
        var models = FakeModels.Empty();
        models.DownloadError = new HttpRequestException();
        var setup = Setup(models);

        setup.ContinueCommand.Execute(null);
        await Until(() => setup.HasError);
        Assert.Contains("Check your internet connection", setup.Error);
        Assert.False(setup.CanContinue);
        Assert.True(setup.SkipCommand.CanExecute(null));

        models.DownloadError = null;
        await setup.RetryCommand.RunAsync();
        await Until(() => setup.CanContinue);
        Assert.False(setup.HasError);
    }

    [Fact]
    public async Task AFailedSpeedTest_FallsBackToTiny()
    {
        var setup = Setup(new FakeModels(), testFails: true);

        setup.ContinueCommand.Execute(null);
        await Until(() => setup.CanContinue);

        Assert.Equal(FakeModels.Tiny, setup.First!.Model.Id);
    }

    [Fact]
    public async Task Finish_StoresTheModel_TheStartupChoice_AndCompletion()
    {
        var setup = Setup(new FakeModels(FakeModels.Tiny, FakeModels.Small), tinyRtf: 0.2);
        setup.ContinueCommand.Execute(null);
        await Until(() => setup.CanContinue);
        setup.ContinueCommand.Execute(null);
        Assert.Equal(SetupStep.Done, setup.Step);
        Assert.True(setup.StartWithWindows);

        setup.StartWithWindows = false;
        setup.StartCommand.Execute(null);

        Assert.True(_settings.Current.SetupCompleted);
        Assert.Equal(FakeModels.Small, _settings.Current.Engine.Model);
        Assert.False(_settings.Current.General.StartWithWindows);
        Assert.Equal([true], _finished);
    }

    [Fact]
    public void Skip_WithNoModel_LeavesSetupToFinish()
    {
        var setup = Setup(FakeModels.Empty());

        setup.SkipCommand.Execute(null);

        Assert.False(_settings.Current.SetupCompleted);
        Assert.Equal([false], _finished);
    }

    [Fact]
    public void Skip_WithAModel_DoesNotAskAgain()
    {
        Setup(new FakeModels()).SkipCommand.Execute(null);

        Assert.True(_settings.Current.SetupCompleted);
    }

    [Fact]
    public async Task Back_ReturnsToWelcome_WithoutCheckingAgain()
    {
        var models = FakeModels.Empty();
        var setup = Setup(models);
        setup.ContinueCommand.Execute(null);
        await Until(() => setup.CanContinue);

        setup.BackCommand.Execute(null);
        setup.ContinueCommand.Execute(null);

        Assert.Equal(SetupStep.Model, setup.Step);
        Assert.Single(models.Downloads);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void IsNeeded_UntilCompletedWithAModel(bool completed, bool hasModel, bool expected) =>
        Assert.Equal(expected, SetupModel.IsNeeded(completed, hasModel ? new FakeModels() : FakeModels.Empty()));

    [Fact]
    public void AppPaths_CanPointAtAFreshProfile()
    {
        var paths = AppPaths.ForCurrentUser(name => name == AppPaths.OverrideVariable ? @"C:\Fresh" : null);

        Assert.Equal(Path.Combine(@"C:\Fresh", "Roaming", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(@"C:\Fresh", "Local", "models"), paths.ModelFolder);
    }
}
