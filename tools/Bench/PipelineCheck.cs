using System.Diagnostics;
using Captly.Audio;
using Captly.Core;
using Captly.Core.Speech;
using Captly.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Bench;

/// <summary>
/// The app's audio path without its window: a WAV at real-time pace through the preprocessor and
/// caption worker into the engine. Reports CPU and how much audio the engine was given, to find
/// where captioning's CPU goes.
/// </summary>
internal static class PipelineCheck
{
    private sealed class CountingEngine(ISpeechEngine inner) : ISpeechEngine
    {
        public long Samples;
        public TimeSpan Busy;

        public string Name => inner.Name;
        public event Action<CaptionEvent>? Caption
        {
            add => inner.Caption += value;
            remove => inner.Caption -= value;
        }

        public void Load(EngineOptions options) => inner.Load(options);

        public void Push(ReadOnlySpan<float> audio16kMono)
        {
            var start = Stopwatch.GetTimestamp();
            inner.Push(audio16kMono);
            Busy += Stopwatch.GetElapsedTime(start);
            Samples += audio16kMono.Length;
        }

        public void Reset() => inner.Reset();
        public void Dispose() => inner.Dispose();
    }

    public static int Run(string wav, int seconds, int updateMs, bool loadOnPool = false, bool reset = false)
    {
        using var http = new HttpClient();
        var manager = new ModelManager(AppPaths.ForCurrentUser().ModelFolder, http, NullLogger<ModelManager>.Instance);
        var tiny = manager.Available.Single(m => m.Architecture == ModelArchitecture.TinyStreaming);
        var engine = new CountingEngine(new MoonshineEngine());
        var options = new EngineOptions(manager.GetModelPath(tiny.Id), tiny.Architecture) { UpdateInterval = TimeSpan.FromMilliseconds(updateMs) };
        if (loadOnPool) Task.Run(() => engine.Load(options)).Wait();
        else engine.Load(options);
        if (reset) engine.Reset();

        var preprocessor = new AudioPreprocessor();
        using var worker = new CaptionWorker(preprocessor, engine, NullLogger<CaptionWorker>.Instance);
        using var source = new FileAudioSource(wav);
        preprocessor.Attach(source);
        worker.Start();

        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        source.Start();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        var used = Process.GetCurrentProcess().TotalProcessorTime - cpu;

        Console.WriteLine($"{seconds} s at {updateMs} ms updates: CPU {used.TotalSeconds / wall.Elapsed.TotalSeconds * 100:F0}% of a core, " +
            $"engine busy {engine.Busy.TotalSeconds / wall.Elapsed.TotalSeconds * 100:F0}%, audio given {engine.Samples / 16000.0:F1} s, dropped {preprocessor.DroppedSamples}");
        return 0;
    }
}
