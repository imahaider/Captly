namespace Captly.Audio;

/// <summary>Lock-free ring buffer for exactly one writer thread and one reader thread.</summary>
public sealed class FloatRingBuffer(int capacity)
{
    private readonly float[] _buffer = new float[capacity];
    private long _written;
    private long _read;

    public int Capacity => _buffer.Length;
    public int Available => (int)(Volatile.Read(ref _written) - Volatile.Read(ref _read));

    /// <summary>Copies as much as fits and returns the count. When full, the newest samples are the ones lost.</summary>
    public int Write(ReadOnlySpan<float> samples)
    {
        var position = _written;
        var free = _buffer.Length - (int)(position - Volatile.Read(ref _read));
        var count = Math.Min(free, samples.Length);
        var start = (int)(position % _buffer.Length);
        var first = Math.Min(count, _buffer.Length - start);
        samples[..first].CopyTo(_buffer.AsSpan(start));
        samples[first..count].CopyTo(_buffer);
        Volatile.Write(ref _written, position + count);
        return count;
    }

    public int Read(Span<float> destination)
    {
        var position = _read;
        var available = (int)(Volatile.Read(ref _written) - position);
        var count = Math.Min(available, destination.Length);
        var start = (int)(position % _buffer.Length);
        var first = Math.Min(count, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(destination);
        _buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
        Volatile.Write(ref _read, position + count);
        return count;
    }
}
