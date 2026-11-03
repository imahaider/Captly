using Captly.Core;
using Captly.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Speech.Tests;

/// <summary>
/// Runs the real library. Skipped unless scripts/get-native.ps1 has run and Tiny is installed, so
/// that tests never download anything.
/// </summary>
public class MoonshineEngineTests
{
    private const int Rate = 16000;

    private static float[] LoadSample()
    {
        var path = Path.Combine(FindRepoRoot(), "samples", "two_cities_16k.wav");
        var bytes = File.ReadAllBytes(path);
        var offset = 12;
        while (BitConverter.ToInt32(bytes, offset) != 0x61746164) offset += 8 + BitConverter.ToInt32(bytes, offset + 4);
        var size = BitConverter.ToInt32(bytes, offset + 4);
        var samples = new float[size / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, offset + 8 + i * 2) / 32768f;
        return samples;
    }

    private static string FindRepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Captly.slnx"))) folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static MoonshineEngine LoadEngine()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "moonshine.dll")))
            Assert.Skip("moonshine.dll is missing. Run scripts/get-native.ps1.");

        using var http = new HttpClient();
        var manager = new ModelManager(AppPaths.ForCurrentUser().ModelFolder, http, NullLogger<ModelManager>.Instance);
        var tiny = manager.Available.Single(m => m.Architecture == ModelArchitecture.TinyStreaming);
        if (!tiny.IsInstalled) Assert.Skip("Tiny is not installed. Run Bench download tiny.");

        var engine = new MoonshineEngine();
        engine.Load(new EngineOptions(manager.GetModelPath(tiny.Id), tiny.Architecture));
        return engine;
    }

    private static void Feed(ISpeechEngine engine, ReadOnlySpan<float> audio)
    {
        const int chunk = Rate / 10;
        for (var position = 0; position < audio.Length; position += chunk)
            engine.Push(audio.Slice(position, Math.Min(chunk, audio.Length - position)));
    }

    [Fact]
    public void Speech_GivesPartialsThenFinals()
    {
        using var engine = LoadEngine();
        var events = new List<CaptionEvent>();
        engine.Caption += events.Add;

        Feed(engine, LoadSample().AsSpan(0, 8 * Rate));
        Feed(engine, new float[2 * Rate]);

        Assert.Contains(events, e => !e.IsFinal);
        var final = events.First(e => e.IsFinal && e.Text.Length > 0);
        Assert.Contains("best of times", final.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Silence_GivesNoCaptions()
    {
        using var engine = LoadEngine();
        var events = new List<CaptionEvent>();
        engine.Caption += events.Add;

        Feed(engine, new float[5 * Rate]);

        Assert.Empty(events);
    }

    [Fact]
    public void Reset_DropsTheOldLineAndStartsAgain()
    {
        var sample = LoadSample();
        using var engine = LoadEngine();
        var events = new List<CaptionEvent>();
        engine.Caption += events.Add;

        // Stop mid-line, in the second clause of the first sentence.
        Feed(engine, sample.AsSpan(0, 4 * Rate));
        Assert.DoesNotContain(events, e => e.IsFinal);

        engine.Reset();
        events.Clear();

        // A jump to somewhere else entirely, as a seek would be.
        Feed(engine, sample.AsSpan(20 * Rate, 8 * Rate));
        Feed(engine, new float[2 * Rate]);

        Assert.NotEmpty(events);
        Assert.DoesNotContain(events, e => e.Text.Contains("best of times", StringComparison.OrdinalIgnoreCase));
        Assert.True(events[0].StreamTime < TimeSpan.FromSeconds(3), $"Stream time should restart, was {events[0].StreamTime}");
    }

    [Fact]
    public void SameTextAfterReset_IsNotTreatedAsARepeat()
    {
        var clip = LoadSample().AsSpan(0, 6 * Rate);
        using var engine = LoadEngine();
        var finals = new List<string>();
        engine.Caption += c => { if (c.IsFinal && c.Text.Length > 0) finals.Add(c.Text); };

        Feed(engine, clip);
        Feed(engine, new float[2 * Rate]);
        var firstCount = finals.Count;
        Assert.True(firstCount > 0);

        engine.Reset();
        Feed(engine, clip);
        Feed(engine, new float[2 * Rate]);

        Assert.Equal(2 * firstCount, finals.Count);
    }

    [Fact]
    public void BadModelFolder_FailsWithAClearError()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "moonshine.dll"))) Assert.Skip("moonshine.dll is missing.");
        using var engine = new MoonshineEngine();

        var error = Assert.Throws<SpeechEngineException>(() =>
            engine.Load(new EngineOptions(Path.Combine(Path.GetTempPath(), "captly-no-such-model"), ModelArchitecture.TinyStreaming)));

        Assert.Contains("Tiny", error.Message);
    }

    [Fact]
    public void Push_BeforeLoad_Fails() =>
        Assert.Throws<InvalidOperationException>(() => new MoonshineEngine().Push(new float[10]));
}
