using Captly.Audio;

namespace Captly.Bench;

internal static class Wav
{
    /// <summary>Reads a WAV and runs it through the app's own preprocessing.</summary>
    public static float[] ReadMono16k(string path)
    {
        var (samples, format) = WavFile.Read(path);
        var preprocessor = new AudioPreprocessor(samples.Length / format.Channels / format.SampleRate + 1);
        preprocessor.Configure(format);
        preprocessor.Process(samples);
        var output = new float[preprocessor.Available];
        preprocessor.Read(output);
        return output;
    }

    public static IEnumerable<ReadOnlyMemory<float>> Chunks(float[] audio, int size)
    {
        for (var position = 0; position < audio.Length; position += size)
            yield return audio.AsMemory(position, Math.Min(size, audio.Length - position));
    }
}
