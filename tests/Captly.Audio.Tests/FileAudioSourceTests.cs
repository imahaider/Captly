using Captly.Core.Audio;

namespace Captly.Audio.Tests;

public sealed class FileAudioSourceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("captly-wav-").FullName;
    private string WavPath => Path.Combine(_folder, "tone.wav");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteTone(int rate, double seconds)
    {
        using var writer = new WavFileWriter(WavPath, rate);
        writer.Write(Signal.Sine(1000, rate, seconds));
    }

    [Fact]
    public void WavFile_ReadsWhatWasWritten()
    {
        WriteTone(16000, 0.5);

        var (samples, format) = WavFile.Read(WavPath);

        Assert.Equal(new WaveFormatInfo(16000, 1), format);
        Assert.Equal(8000, samples.Length);
        Assert.Equal(0.5 / Math.Sqrt(2), Signal.Rms(samples), 2);
    }

    [Fact]
    public void WavFile_RejectsAFileWithNoAudio()
    {
        File.WriteAllBytes(WavPath, new byte[12]);

        Assert.Throws<InvalidDataException>(() => WavFile.Read(WavPath));
    }

    [Fact]
    public async Task Source_AnnouncesItsFormat_ThenDeliversEverySample()
    {
        WriteTone(16000, 0.3);
        using var source = new FileAudioSource(WavPath);
        WaveFormatInfo? announced = null;
        var received = 0;
        using var done = new SemaphoreSlim(0);
        source.FormatChanged += format => announced = format;
        source.SamplesAvailable += samples =>
        {
            if (Interlocked.Add(ref received, samples.Length) >= 4800) done.Release();
        };

        source.Start();
        await done.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(new WaveFormatInfo(16000, 1), announced);
        Assert.Equal(4800, received);
    }

    [Fact]
    public void Source_StopsWhenAsked()
    {
        WriteTone(16000, 30);
        using var source = new FileAudioSource(WavPath);

        source.Start();
        source.Stop();

        Assert.Equal("tone.wav", source.DisplayName);
    }
}
