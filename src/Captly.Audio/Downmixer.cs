namespace Captly.Audio;

public static class Downmixer
{
    private const float Center = 0.7071f;
    private const float Surround = 0.3536f;

    /// <summary>
    /// Mixes interleaved frames to mono. Surround layouts use the ITU downmix and skip the LFE,
    /// because a plain average would bury dialogue, which sits in the centre channel. Channel
    /// order is the Windows order: FL FR FC LFE BL BR SL SR.
    /// </summary>
    public static void ToMono(ReadOnlySpan<float> interleaved, int channels, Span<float> mono)
    {
        var frames = interleaved.Length / channels;
        switch (channels)
        {
            case 1:
                interleaved[..frames].CopyTo(mono);
                break;
            case 2:
                for (var i = 0; i < frames; i++)
                    mono[i] = (interleaved[i * 2] + interleaved[i * 2 + 1]) * 0.5f;
                break;
            case 6:
            case 8:
                for (var i = 0; i < frames; i++)
                {
                    var f = interleaved.Slice(i * channels, channels);
                    var value = (f[0] + f[1]) * 0.5f + f[2] * Center + (f[4] + f[5]) * Surround;
                    if (channels == 8) value += (f[6] + f[7]) * Surround;
                    mono[i] = Math.Clamp(value, -1f, 1f);
                }
                break;
            default:
                for (var i = 0; i < frames; i++)
                {
                    var sum = 0f;
                    foreach (var sample in interleaved.Slice(i * channels, channels)) sum += sample;
                    mono[i] = sum / channels;
                }
                break;
        }
    }
}
