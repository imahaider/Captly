using Captly.Core.Audio;

namespace Captly.Audio;

public sealed record DumpResult(double SecondsWritten, float Peak, long DroppedSamples, WaveFormatInfo SourceFormat);

/// <summary>Records a source through the full preprocessing path into a 16 kHz WAV, for listening checks.</summary>
public static class DebugAudioDump
{
    public static async Task<DumpResult> RunAsync(IAudioSource source, string path, TimeSpan duration, CancellationToken ct = default)
    {
        var preprocessor = new AudioPreprocessor();
        preprocessor.Attach(source);

        using var writer = new WavFileWriter(path, AudioPreprocessor.OutputRate);
        var buffer = new float[AudioPreprocessor.OutputRate];
        long samples = 0;
        var peak = 0f;

        void Drain()
        {
            int count;
            while ((count = preprocessor.Read(buffer)) > 0)
            {
                var block = buffer.AsSpan(0, count);
                writer.Write(block);
                samples += count;
                foreach (var sample in block) peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        source.Start();
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(100, CancellationToken.None);
            Drain();
        }
        source.Stop();
        Drain();

        return new DumpResult(samples / (double)AudioPreprocessor.OutputRate, peak, preprocessor.DroppedSamples, source.Format);
    }
}
