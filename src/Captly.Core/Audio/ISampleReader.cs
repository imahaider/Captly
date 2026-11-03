namespace Captly.Core.Audio;

/// <summary>Where the inference thread collects 16 kHz mono audio.</summary>
public interface ISampleReader
{
    /// <summary>Copies what is ready, without waiting, and returns the count.</summary>
    int Read(Span<float> destination);
}
