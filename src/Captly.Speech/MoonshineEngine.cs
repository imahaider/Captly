using System.Globalization;
using Captly.Core.Speech;

namespace Captly.Speech;

public sealed unsafe class MoonshineEngine : ISpeechEngine
{
    private const int SampleRate = 16000;

    private readonly LineTracker _tracker = new();
    private LineSnapshot[] _snapshots = new LineSnapshot[64];
    private int _transcriber = -1;
    private int _stream = -1;
    private int _samplesPerUpdate;
    private int _samplesSinceUpdate;
    private long _samplesHeard;

    public string Name { get; private set; } = "Moonshine";

    public event Action<CaptionEvent>? Caption;

    public void Load(EngineOptions options)
    {
        MoonshineNative.ForceSingleThread();
        FreeAll();

        var (arch, name) = options.Architecture switch
        {
            ModelArchitecture.TinyStreaming => (MoonshineNative.ArchTinyStreaming, "Moonshine Tiny Streaming"),
            ModelArchitecture.SmallStreaming => (MoonshineNative.ArchSmallStreaming, "Moonshine Small Streaming"),
            ModelArchitecture.MediumStreaming => (MoonshineNative.ArchMediumStreaming, "Moonshine Medium Streaming"),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var interval = options.UpdateInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        fixed (byte* path = MoonshineNative.Utf8(options.ModelPath),
               intervalName = MoonshineNative.Utf8("transcription_interval"),
               intervalValue = MoonshineNative.Utf8(interval),
               audioName = MoonshineNative.Utf8("return_audio_data"),
               audioValue = MoonshineNative.Utf8("false"))
        {
            var settings = stackalloc MoonshineNative.Option[]
            {
                new() { Name = intervalName, Value = intervalValue },
                new() { Name = audioName, Value = audioValue },
            };
            _transcriber = MoonshineNative.LoadTranscriber(path, arch, settings, 2, MoonshineNative.HeaderVersion);
        }
        if (_transcriber < 0) throw new SpeechEngineException($"Could not load {name}: {MoonshineNative.Text(MoonshineNative.ErrorToString(_transcriber))}");

        Name = name;
        _samplesPerUpdate = (int)(options.UpdateInterval.TotalSeconds * SampleRate);
        StartStream();
    }

    public void Push(ReadOnlySpan<float> audio16kMono)
    {
        if (_stream < 0) throw new InvalidOperationException("Load must run before Push.");

        fixed (float* audio = audio16kMono)
            Check(MoonshineNative.AddAudio(_transcriber, _stream, audio, (ulong)audio16kMono.Length, SampleRate, 0));

        _samplesSinceUpdate += audio16kMono.Length;
        _samplesHeard += audio16kMono.Length;
        if (_samplesSinceUpdate < _samplesPerUpdate) return;
        _samplesSinceUpdate = 0;

        MoonshineNative.Transcript* transcript = null;
        Check(MoonshineNative.TranscribeStream(_transcriber, _stream, 0, &transcript));
        Publish(transcript);
    }

    public void Reset()
    {
        if (_stream < 0) return;
        MoonshineNative.StopStream(_transcriber, _stream);
        MoonshineNative.FreeStream(_transcriber, _stream);
        _tracker.Reset();
        StartStream();
    }

    public void Dispose() => FreeAll();

    private void StartStream()
    {
        _stream = MoonshineNative.CreateStream(_transcriber, 0);
        if (_stream < 0) throw new SpeechEngineException(MoonshineNative.Text(MoonshineNative.ErrorToString(_stream)));
        Check(MoonshineNative.StartStream(_transcriber, _stream));
        _samplesSinceUpdate = 0;
        _samplesHeard = 0;
    }

    private void Publish(MoonshineNative.Transcript* transcript)
    {
        var count = (int)transcript->LineCount;
        if (_snapshots.Length < count) Array.Resize(ref _snapshots, count * 2);

        // Text is copied here, because the engine owns it only until the next call.
        for (var i = 0; i < count; i++)
        {
            ref var line = ref transcript->Lines[i];
            _snapshots[i] = new LineSnapshot(MoonshineNative.Text(line.Text), TimeSpan.FromSeconds(line.StartTime), line.IsComplete != 0, TimeSpan.FromSeconds(line.Duration));
        }

        var heard = TimeSpan.FromSeconds(_samplesHeard / (double)SampleRate);
        foreach (var caption in _tracker.Update(_snapshots.AsSpan(0, count))) Caption?.Invoke(caption with { HeardUpTo = heard });
    }

    private void FreeAll()
    {
        if (_stream >= 0) MoonshineNative.FreeStream(_transcriber, _stream);
        if (_transcriber >= 0) MoonshineNative.FreeTranscriber(_transcriber);
        _stream = -1;
        _transcriber = -1;
    }

    private static void Check(int error)
    {
        if (error != 0) throw new SpeechEngineException(MoonshineNative.Text(MoonshineNative.ErrorToString(error)));
    }
}

public sealed class SpeechEngineException(string message) : Exception(message);
