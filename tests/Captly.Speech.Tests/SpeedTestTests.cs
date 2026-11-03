using Captly.Core;
using Captly.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Speech.Tests;

public class SpeedTestTests
{
    [Fact]
    public void SampleAudio_IsTenSecondsOfSpeech()
    {
        var audio = SpeedTest.SampleAudio();

        Assert.Equal(160000, audio.Length);
        Assert.InRange(audio.Max(Math.Abs), 0.1f, 1f);
    }

    // Runs the real engine, so it skips like MoonshineEngineTests when the library or Tiny is missing.
    [Fact]
    public void Run_MeasuresARealTimeFactor()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "moonshine.dll"))) Assert.Skip("moonshine.dll is missing.");
        using var http = new HttpClient();
        var manager = new ModelManager(AppPaths.ForCurrentUser().ModelFolder, http, NullLogger<ModelManager>.Instance);
        var tiny = manager.Available.Single(m => m.Architecture == ModelArchitecture.TinyStreaming);
        if (!tiny.IsInstalled) Assert.Skip("Tiny is not installed. Run Bench download tiny.");

        var result = SpeedTest.Run(tiny, manager.GetModelPath(tiny.Id), TestContext.Current.CancellationToken);

        Assert.Equal(tiny.Id, result.ModelId);
        Assert.Equal(34, result.MillionParameters);
        Assert.InRange(result.RealTimeFactor, 0.01, 5);
    }

    [Fact]
    public void Run_StopsWhenCancelled()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "moonshine.dll"))) Assert.Skip("moonshine.dll is missing.");
        using var http = new HttpClient();
        var manager = new ModelManager(AppPaths.ForCurrentUser().ModelFolder, http, NullLogger<ModelManager>.Instance);
        var tiny = manager.Available.Single(m => m.Architecture == ModelArchitecture.TinyStreaming);
        if (!tiny.IsInstalled) Assert.Skip("Tiny is not installed. Run Bench download tiny.");

        Assert.Throws<OperationCanceledException>(() => SpeedTest.Run(tiny, manager.GetModelPath(tiny.Id), new CancellationToken(true)));
    }
}
