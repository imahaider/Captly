using System.Text;

namespace Captly.Audio;

/// <summary>Writes mono 16-bit PCM. The header sizes are filled in on dispose.</summary>
public sealed class WavFileWriter : IDisposable
{
    private const int HeaderBytes = 44;

    private readonly BinaryWriter _writer;
    private int _dataBytes;

    public WavFileWriter(string path, int sampleRate)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        _writer = new BinaryWriter(File.Create(path));

        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        _writer.Write(16);
        _writer.Write((short)1);
        _writer.Write((short)1);
        _writer.Write(sampleRate);
        _writer.Write(sampleRate * 2);
        _writer.Write((short)2);
        _writer.Write((short)16);
        _writer.Write(Encoding.ASCII.GetBytes("data"));
        _writer.Write(0);
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
            _writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        _dataBytes += samples.Length * 2;
    }

    public void Dispose()
    {
        _writer.Seek(4, SeekOrigin.Begin);
        _writer.Write(HeaderBytes - 8 + _dataBytes);
        _writer.Seek(HeaderBytes - 4, SeekOrigin.Begin);
        _writer.Write(_dataBytes);
        _writer.Dispose();
    }
}
