using System.Numerics;

namespace Captly.Audio;

/// <summary>
/// Streaming polyphase resampler using a Kaiser-windowed sinc low-pass. It never allocates after
/// construction, so it is safe on the capture thread. Output is delayed by
/// <see cref="DelayInputFrames"/> input frames.
/// </summary>
public sealed class Resampler
{
    private const int ZeroCrossings = 24;
    private const double CutoffRatio = 0.925;
    private const double KaiserBeta = 7.0;

    private readonly int _up;
    private readonly int _down;
    private readonly int _taps;
    private readonly float[] _filters;
    private readonly float[] _delay;
    private int _delayPosition;
    private int _phase;

    public Resampler(int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);

        var divisor = Gcd(inputRate, outputRate);
        _up = outputRate / divisor;
        _down = inputRate / divisor;

        if (_up == 1 && _down == 1)
        {
            _taps = 0;
            _filters = [];
            _delay = [];
            return;
        }

        var wider = Math.Max(_up, _down);
        _taps = 2 * ZeroCrossings * wider / _up;
        _filters = BuildFilters(_up, _taps, CutoffRatio / wider);
        _delay = new float[_taps * 2];
    }

    public double DelayInputFrames => _taps == 0 ? 0 : (_taps * _up - 1) / 2.0 / _up;

    /// <summary>The most output frames <see cref="Process"/> can produce for this much input.</summary>
    public int MaxOutputFrames(int inputFrames) => (int)((long)inputFrames * _up / _down) + 1;

    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_taps == 0)
        {
            input.CopyTo(output);
            return input.Length;
        }

        var written = 0;
        foreach (var sample in input)
        {
            // Mirrored delay line: the newest _taps samples are always one contiguous run.
            _delayPosition = _delayPosition == 0 ? _taps - 1 : _delayPosition - 1;
            _delay[_delayPosition] = sample;
            _delay[_delayPosition + _taps] = sample;
            var recent = _delay.AsSpan(_delayPosition, _taps);

            while (_phase < _up)
            {
                output[written++] = Dot(_filters.AsSpan(_phase * _taps, _taps), recent);
                _phase += _down;
            }
            _phase -= _up;
        }
        return written;
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var total = Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
            total += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);

        var result = Vector.Sum(total);
        for (; i < a.Length; i++) result += a[i] * b[i];
        return result;
    }

    // Filters are stored per phase, so filters[phase * taps + k] is h(k * up + phase).
    private static float[] BuildFilters(int up, int taps, double cutoff)
    {
        var length = taps * up;
        var prototype = new double[length];
        var center = (length - 1) / 2.0;
        var sum = 0.0;
        for (var j = 0; j < length; j++)
        {
            var offset = j - center;
            var position = offset / (center + 0.5);
            var window = BesselI0(KaiserBeta * Math.Sqrt(Math.Max(0, 1 - position * position))) / BesselI0(KaiserBeta);
            prototype[j] = cutoff * Sinc(cutoff * offset) * window;
            sum += prototype[j];
        }

        var filters = new float[length];
        for (var phase = 0; phase < up; phase++)
        for (var k = 0; k < taps; k++)
            filters[phase * taps + k] = (float)(prototype[k * up + phase] * up / sum);
        return filters;
    }

    private static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1;
        for (var k = 1; k < 32; k++)
        {
            term *= x / (2 * k) * (x / (2 * k));
            sum += term;
        }
        return sum;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
