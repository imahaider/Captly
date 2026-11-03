using Captly.Core.Audio;

namespace Captly.Audio.Tests;

public class AudioPreprocessorTests
{
    private static float[] ToStereo(float[] mono) => mono.SelectMany(s => new[] { s, s }).ToArray();

    private static float[] ReadAll(AudioPreprocessor preprocessor)
    {
        var output = new float[preprocessor.Available];
        preprocessor.Read(output);
        return output;
    }

    [Theory]
    [InlineData(48000, 2)]
    [InlineData(44100, 2)]
    [InlineData(48000, 6)]
    [InlineData(96000, 8)]
    [InlineData(16000, 1)]
    public void DeviceFormats_ComeOutAs16kMono(int rate, int channels)
    {
        var preprocessor = new AudioPreprocessor();
        preprocessor.Configure(new WaveFormatInfo(rate, channels));

        preprocessor.Process(new float[rate * channels]);

        Assert.InRange(preprocessor.Available, 15999, 16001);
    }

    [Fact]
    public void Tone_SurvivesAtTheSameLevel()
    {
        var preprocessor = new AudioPreprocessor();
        preprocessor.Configure(new WaveFormatInfo(48000, 2));

        preprocessor.Process(ToStereo(Signal.Sine(1000, 48000, 1)));

        var output = ReadAll(preprocessor);
        Assert.Equal(0.5 / Math.Sqrt(2), Signal.Rms(output.AsSpan(4000, 8000)), 3);
    }

    [Fact]
    public void LargeBuffers_AreSplitWithoutLoss()
    {
        var preprocessor = new AudioPreprocessor();
        preprocessor.Configure(new WaveFormatInfo(48000, 2));

        preprocessor.Process(new float[48000 * 2 * 5]);

        Assert.InRange(preprocessor.Available, 79999, 80001);
    }

    [Fact]
    public void Overflow_IsCounted()
    {
        var preprocessor = new AudioPreprocessor(bufferSeconds: 1);
        preprocessor.Configure(new WaveFormatInfo(16000, 1));

        preprocessor.Process(new float[20000]);

        Assert.Equal(16000, preprocessor.Available);
        Assert.Equal(4000, preprocessor.DroppedSamples);
    }

    [Fact]
    public void FormatChange_KeepsWorkingOnTheSameBuffer()
    {
        var preprocessor = new AudioPreprocessor();
        preprocessor.Configure(new WaveFormatInfo(48000, 2));
        preprocessor.Process(new float[48000 * 2]);

        preprocessor.Configure(new WaveFormatInfo(44100, 6));
        preprocessor.Process(new float[44100 * 6]);

        Assert.InRange(preprocessor.Available, 31998, 32002);
    }

    [Fact]
    public void Process_BeforeConfigure_Fails() =>
        Assert.Throws<InvalidOperationException>(() => new AudioPreprocessor().Process(new float[4]));
}
