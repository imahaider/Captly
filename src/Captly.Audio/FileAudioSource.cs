using System.Text;
using Captly.Core.Audio;

namespace Captly.Audio;

public static class WavFile
{
    /// <summary>Reads a PCM or float WAV into interleaved floats.</summary>
    public static (float[] Samples, WaveFormatInfo Format) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12);
        SampleFormat format = default;
        int channels = 0, rate = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var tag = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadBytes(6);
                var bits = reader.ReadInt16();
                reader.ReadBytes(size - 16);
                format = (tag, bits) switch
                {
                    (3, 32) => SampleFormat.Float32,
                    (1, 16) => SampleFormat.Pcm16,
                    (1, 24) => SampleFormat.Pcm24,
                    (1, 32) => SampleFormat.Pcm32,
                    _ => throw new InvalidDataException($"Unsupported WAV: tag {tag}, {bits} bits"),
                };
            }
            else if (id == "data")
            {
                var bytes = reader.ReadBytes(size);
                var samples = new float[bytes.Length / SampleConverter.BytesPerSample(format)];
                SampleConverter.ToFloat(bytes, format, samples);
                return (samples, new WaveFormatInfo(rate, channels));
            }
            else
            {
                reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("No data chunk");
    }
}

/// <summary>
/// Plays a WAV into the pipeline at real-time pace, without any sound, for demos and checks.
/// Looping plays it again and again, for long runs.
/// </summary>
public sealed class FileAudioSource(string path, bool loop = false) : IAudioSource
{
    private static readonly TimeSpan ChunkTime = TimeSpan.FromMilliseconds(100);

    private readonly (float[] Samples, WaveFormatInfo Format) _wav = WavFile.Read(path);
    private volatile bool _running;
    private Thread? _thread;

    public string DisplayName => Path.GetFileName(path);
    public WaveFormatInfo Format => _wav.Format;

    public event Action<WaveFormatInfo>? FormatChanged;
    public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

    public void Start()
    {
        _running = true;
        _thread = new Thread(Play) { IsBackground = true, Name = "Captly file audio" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    public void Dispose() => Stop();

    private void Play()
    {
        FormatChanged?.Invoke(Format);
        var chunk = (int)(Format.SampleRate * ChunkTime.TotalSeconds) * Format.Channels;
        for (var position = 0; _running && position < _wav.Samples.Length; position += chunk)
        {
            if (loop && position + chunk >= _wav.Samples.Length) position = 0;
            SamplesAvailable?.Invoke(_wav.Samples.AsMemory(position, Math.Min(chunk, _wav.Samples.Length - position)));
            Thread.Sleep(ChunkTime);
        }
    }
}
