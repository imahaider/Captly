using System.Buffers.Binary;
using System.Diagnostics;
using Captly.Core.Speech;

namespace Captly.Speech;

/// <summary>
/// The Speech Model screen's speed test, and the input to <see cref="ModelRecommender"/>. Runs a
/// model over 10 s of built-in speech as fast as it will go, the way D6 and D16 were measured.
/// The clip is the start of the Phase 0 sample, as 16 kHz 16-bit mono.
/// </summary>
public static class SpeedTest
{
    private const int SampleRate = 16000;
    private const int ChunkSamples = 1600;

    public static float[] SampleAudio()
    {
        using var stream = typeof(SpeedTest).Assembly.GetManifestResourceStream("Captly.Speech.speedtest.pcm")
            ?? throw new InvalidOperationException("speedtest.pcm is not embedded.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        var audio = new float[bytes.Length / 2];
        for (var i = 0; i < audio.Length; i++) audio[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2)) / 32768f;
        return audio;
    }

    /// <summary>Loads the model on its own, so it can run beside live captions, and measures it.</summary>
    public static Task<BenchResult> RunAsync(ModelInfo model, string modelPath, CancellationToken ct) =>
        Task.Factory.StartNew(() => Run(model, modelPath, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public static BenchResult Run(ModelInfo model, string modelPath, CancellationToken ct)
    {
        using var engine = new MoonshineEngine();
        engine.Load(new EngineOptions(modelPath, model.Architecture));
        var audio = SampleAudio();

        var wall = Stopwatch.StartNew();
        for (var start = 0; start < audio.Length; start += ChunkSamples)
        {
            ct.ThrowIfCancellationRequested();
            engine.Push(audio.AsSpan(start, Math.Min(ChunkSamples, audio.Length - start)));
        }
        wall.Stop();

        return new BenchResult(model.Id, model.MillionParameters, wall.Elapsed.TotalSeconds / ((double)audio.Length / SampleRate));
    }
}
