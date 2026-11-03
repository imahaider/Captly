namespace Captly.Audio.Tests;

public class SampleConverterTests
{
    private const float Tolerance = 1e-6f;

    private static float[] Convert(byte[] bytes, SampleFormat format)
    {
        var output = new float[bytes.Length / SampleConverter.BytesPerSample(format)];
        Assert.Equal(output.Length, SampleConverter.ToFloat(bytes, format, output));
        return output;
    }

    [Fact]
    public void Pcm16_CoversTheFullRange()
    {
        var output = Convert([0x00, 0x80, 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x40], SampleFormat.Pcm16);
        Assert.Equal(-1f, output[0]);
        Assert.Equal(0f, output[1]);
        Assert.Equal(32767f / 32768f, output[2], Tolerance);
        Assert.Equal(0.5f, output[3]);
    }

    [Fact]
    public void Pcm24_SignExtends()
    {
        var output = Convert([0x00, 0x00, 0x80, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x40], SampleFormat.Pcm24);
        Assert.Equal(-1f, output[0]);
        Assert.Equal(-1f / 8388608f, output[1], Tolerance);
        Assert.Equal(0.5f, output[2]);
    }

    [Fact]
    public void Pcm32_CoversTheFullRange()
    {
        var output = Convert([0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x40], SampleFormat.Pcm32);
        Assert.Equal(-1f, output[0]);
        Assert.Equal(0.5f, output[1]);
    }

    [Fact]
    public void Float32_PassesThrough()
    {
        var bytes = BitConverter.GetBytes(0.25f).Concat(BitConverter.GetBytes(-0.75f)).ToArray();
        Assert.Equal([0.25f, -0.75f], Convert(bytes, SampleFormat.Float32));
    }

    [Fact]
    public void PartialTrailingSample_IsIgnored()
    {
        var output = new float[2];
        Assert.Equal(1, SampleConverter.ToFloat([0x00, 0x40, 0xAB], SampleFormat.Pcm16, output));
        Assert.Equal(0.5f, output[0]);
    }
}
