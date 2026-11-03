using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Captly.Audio;

public enum SampleFormat { Float32, Pcm16, Pcm24, Pcm32 }

public static class SampleConverter
{
    public static int BytesPerSample(SampleFormat format) => format switch
    {
        SampleFormat.Pcm16 => 2,
        SampleFormat.Pcm24 => 3,
        _ => 4,
    };

    /// <summary>Converts whole samples to floats between -1 and 1. Returns the sample count.</summary>
    public static int ToFloat(ReadOnlySpan<byte> source, SampleFormat format, Span<float> destination)
    {
        var count = source.Length / BytesPerSample(format);
        switch (format)
        {
            case SampleFormat.Float32:
                MemoryMarshal.Cast<byte, float>(source)[..count].CopyTo(destination);
                break;
            case SampleFormat.Pcm16:
                for (var i = 0; i < count; i++)
                    destination[i] = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 2)..]) / 32768f;
                break;
            case SampleFormat.Pcm24:
                for (var i = 0; i < count; i++)
                {
                    var o = i * 3;
                    var value = source[o] | source[o + 1] << 8 | (sbyte)source[o + 2] << 16;
                    destination[i] = value / 8388608f;
                }
                break;
            case SampleFormat.Pcm32:
                for (var i = 0; i < count; i++)
                    destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]) / 2147483648f;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
        return count;
    }
}
