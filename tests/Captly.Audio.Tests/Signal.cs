namespace Captly.Audio.Tests;

internal static class Signal
{
    public static float[] Sine(double hz, int rate, double seconds, float amplitude = 0.5f)
    {
        var samples = new float[(int)(rate * seconds)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = amplitude * (float)Math.Sin(2 * Math.PI * hz * i / rate);
        return samples;
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        var energy = 0.0;
        foreach (var sample in samples) energy += sample * sample;
        return Math.Sqrt(energy / samples.Length);
    }

    public static double Decibels(double ratio) => 20 * Math.Log10(Math.Max(ratio, 1e-12));
}
