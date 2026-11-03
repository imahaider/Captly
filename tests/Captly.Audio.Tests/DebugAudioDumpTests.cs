using Captly.Core.Audio;

namespace Captly.Audio.Tests;

public sealed class DebugAudioDumpTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("captly-dump-").FullName;
    private string WavPath => Path.Combine(_folder, "sub", "debug.wav");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeSource(WaveFormatInfo format, float[] samples) : IAudioSource
    {
        public string DisplayName => "Fake";
        public WaveFormatInfo Format => format;
        public event Action<WaveFormatInfo>? FormatChanged;
        public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

        public void Start()
        {
            FormatChanged?.Invoke(format);
            SamplesAvailable?.Invoke(samples);
        }

        public void Stop() { }
        public void Dispose() { }
    }

    private async Task<DumpResult> Run(int rate, int channels, double seconds)
    {
        var mono = Signal.Sine(1000, rate, seconds);
        var interleaved = mono.SelectMany(s => Enumerable.Repeat(s, channels)).ToArray();
        using var source = new FakeSource(new WaveFormatInfo(rate, channels), interleaved);
        return await DebugAudioDump.RunAsync(source, WavPath, TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WritesA16kMonoPcmWav()
    {
        var result = await Run(48000, 2, 2);

        var bytes = File.ReadAllBytes(WavPath);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 20));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(16000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(32000, BitConverter.ToInt32(bytes, 28));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
        Assert.Equal(bytes.Length - 44, BitConverter.ToInt32(bytes, 40));
        Assert.Equal((bytes.Length - 44) / 2.0 / 16000, result.SecondsWritten, 3);
    }

    [Fact]
    public async Task ReportsWhatItRecorded()
    {
        var result = await Run(44100, 2, 2);

        Assert.Equal(2.0, result.SecondsWritten, 1);
        Assert.Equal(0.5f, result.Peak, 2);
        Assert.Equal(0, result.DroppedSamples);
        Assert.Equal(new WaveFormatInfo(44100, 2), result.SourceFormat);
    }

    [Fact]
    public async Task ASilentSource_IsNotAnError()
    {
        using var source = new FakeSource(new WaveFormatInfo(48000, 2), []);

        var result = await DebugAudioDump.RunAsync(source, WavPath, TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SecondsWritten);
        Assert.Equal(44, new FileInfo(WavPath).Length);
    }
}
