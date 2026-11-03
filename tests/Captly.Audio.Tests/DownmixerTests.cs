namespace Captly.Audio.Tests;

public class DownmixerTests
{
    private const float Tolerance = 1e-3f;

    private static float[] Mix(float[] interleaved, int channels)
    {
        var mono = new float[interleaved.Length / channels];
        Downmixer.ToMono(interleaved, channels, mono);
        return mono;
    }

    [Fact]
    public void Mono_IsUnchanged() => Assert.Equal([0.1f, -0.2f], Mix([0.1f, -0.2f], 1));

    [Fact]
    public void Stereo_Averages() => Assert.Equal([0.5f, 0f], Mix([1f, 0f, 0.5f, -0.5f], 2));

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void OtherLayouts_Average(int channels)
    {
        var frame = Enumerable.Repeat(0.3f, channels).ToArray();
        Assert.Equal(0.3f, Mix(frame, channels)[0], Tolerance);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Surround_KeepsDialogueInTheCentreChannel(int channels)
    {
        var frame = new float[channels];
        frame[2] = 0.5f;
        Assert.Equal(0.3536f, Mix(frame, channels)[0], Tolerance);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Surround_IgnoresTheLfe(int channels)
    {
        var frame = new float[channels];
        frame[3] = 1f;
        Assert.Equal(0f, Mix(frame, channels)[0]);
    }

    [Fact]
    public void Surround_FullFrontMatchesStereo() =>
        Assert.Equal(0.5f, Mix([0.5f, 0.5f, 0, 0, 0, 0], 6)[0], Tolerance);

    [Fact]
    public void Surround_NeverExceedsFullScale() =>
        Assert.Equal(1f, Mix(Enumerable.Repeat(1f, 8).ToArray(), 8)[0]);
}
