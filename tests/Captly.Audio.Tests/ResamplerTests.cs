namespace Captly.Audio.Tests;

public class ResamplerTests
{
    private const int Target = 16000;

    private static float[] Resample(float[] input, int inputRate, int outputRate = Target)
    {
        var resampler = new Resampler(inputRate, outputRate);
        var output = new float[resampler.MaxOutputFrames(input.Length)];
        return output[..resampler.Process(input, output)];
    }

    // Skips the filter's start-up, so only the settled signal is measured.
    private static double OutputGainDb(double hz, int inputRate)
    {
        var output = Resample(Signal.Sine(hz, inputRate, 1), inputRate);
        return Signal.Decibels(Signal.Rms(output.AsSpan(4000, 8000)) / (0.5 / Math.Sqrt(2)));
    }

    public static TheoryData<int> CommonRates => [44100, 48000, 88200, 96000, 192000];

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(22050)]
    [InlineData(8000)]
    public void OutputLength_FollowsTheRatio(int rate)
    {
        var output = Resample(new float[rate], rate);
        Assert.InRange(output.Length, Target - 1, Target + 1);
    }

    [Fact]
    public void SameRate_PassesThroughUntouched()
    {
        var input = Signal.Sine(440, Target, 0.1);
        Assert.Equal(input, Resample(input, Target));
    }

    [Theory]
    [MemberData(nameof(CommonRates))]
    public void SpeechBand_IsFlat(int rate)
    {
        foreach (var hz in new[] { 100.0, 1000.0, 4000.0, 6000.0 })
            Assert.InRange(OutputGainDb(hz, rate), -0.1, 0.1);
    }

    [Theory]
    [MemberData(nameof(CommonRates))]
    public void AboveNyquist_DoesNotAlias(int rate)
    {
        foreach (var hz in new[] { 8500.0, 10000.0, 15000.0, 20000.0 })
            Assert.True(OutputGainDb(hz, rate) < -65, $"{hz} Hz at {rate} Hz");
    }

    [Fact]
    public void ToneComesOutAtTheSameFrequency()
    {
        var output = Resample(Signal.Sine(1000, 48000, 1), 48000).AsSpan(4000, 8000);

        var crossings = 0;
        for (var i = 1; i < output.Length; i++)
            if (output[i - 1] < 0 && output[i] >= 0) crossings++;

        Assert.InRange(crossings, 499, 501);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void Delay_MatchesTheReportedValue(int rate)
    {
        var input = new float[rate / 2];
        input[rate / 4] = 1;
        var resampler = new Resampler(rate, Target);
        var output = new float[resampler.MaxOutputFrames(input.Length)];
        resampler.Process(input, output);

        var peak = Array.IndexOf(output, output.MaxBy(Math.Abs));
        var expected = (rate / 4 + resampler.DelayInputFrames) * Target / rate;
        Assert.InRange(peak, expected - 1.5, expected + 1.5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(480)]
    [InlineData(4096)]
    public void AnyChunking_GivesIdenticalOutput(int chunk)
    {
        var input = Signal.Sine(1000, 44100, 0.5);
        var expected = Resample(input, 44100);

        var resampler = new Resampler(44100, Target);
        var output = new float[resampler.MaxOutputFrames(input.Length) + 1];
        var written = 0;
        for (var position = 0; position < input.Length; position += chunk)
        {
            var part = input.AsSpan(position, Math.Min(chunk, input.Length - position));
            written += resampler.Process(part, output.AsSpan(written));
        }

        Assert.Equal(expected, output[..written]);
    }

    [Theory]
    [InlineData(0, 16000)]
    [InlineData(48000, -1)]
    public void BadRates_AreRejected(int input, int output) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resampler(input, output));
}
