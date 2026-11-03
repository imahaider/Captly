using System.Diagnostics;
using Captly.Audio;
using Captly.Core.Audio;
using Captly.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Bench;

/// <summary>Hardware checks for per-app capture, which unit tests cannot reach.</summary>
internal static class SourceChecks
{
    private const int Rate = 48000;

    /// <summary>Prints what Windows reports as playing, and what Auto would caption, once a second.</summary>
    public static int Apps(int seconds)
    {
        using var factory = new AudioSourceFactory(NullLoggerFactory.Instance);
        Console.WriteLine($"Per-app capture available: {factory.SupportsPerProcess}");
        var selector = new SourceSelector();
        for (var i = 0; i < seconds; i++)
        {
            var apps = factory.ListActiveApps();
            selector.Update(new SelectorInput(apps, new SourceSettings(), factory.SupportsPerProcess, factory.IsRunning));
            Console.WriteLine($"[{i,3}s] captions: {selector.Current?.DisplayName ?? "nothing"}");
            foreach (var app in apps)
                Console.WriteLine($"       {app.DisplayName,-28} {app.ExeName,-22} root {app.RootPid,6}  peak {app.PeakLevel:F3}  {app.Subtitle}");
            Thread.Sleep(1000);
        }
        return 0;
    }

    /// <summary>
    /// Plays two quiet tones from two processes, captures one through the app's own source, and
    /// measures both. Pass: the other app's tone is at least 40 dB below the target's.
    /// </summary>
    public static int Isolate()
    {
        var folder = Directory.CreateTempSubdirectory("captly-isolate-").FullName;
        var target = Play(Tone(folder, "target", 440));
        var other = Play(Tone(folder, "other", 1000));
        try
        {
            Thread.Sleep(1500);
            using var loggers = LoggerFactory.Create(_ => { });
            using var factory = new AudioSourceFactory(loggers);
            Console.WriteLine($"Per-app capture available: {factory.SupportsPerProcess}");

            var listed = factory.ListActiveApps();
            Console.WriteLine($"Apps seen: {string.Join(", ", listed.Select(a => $"{a.DisplayName} ({a.RootPid}, peak {a.PeakLevel:F3})"))}");

            // The system mix, captured alongside, shows whether a level difference comes from Windows' output path.
            var system = Task.Run(() => Capture(factory.CreateSystemWide(), TimeSpan.FromSeconds(3)));
            var samples = Capture(factory.CreateForProcess(target.Id), TimeSpan.FromSeconds(3));
            var mix = system.GetAwaiter().GetResult();
            Console.WriteLine($"System mix, same moment: 440 Hz {Goertzel(mix, 440):F1} dBFS, 1000 Hz {Goertzel(mix, 1000):F1} dBFS, peak {mix.DefaultIfEmpty().Max(Math.Abs):F3}");
            Console.WriteLine($"App capture peak {samples.DefaultIfEmpty().Max(Math.Abs):F3}, samples at full scale: {samples.Count(s => Math.Abs(s) >= 0.999f)}");
            var targetDb = Goertzel(samples, 440);
            var otherDb = Goertzel(samples, 1000);
            Console.WriteLine($"Captured {samples.Count / (double)Rate:F1}s from PID {target.Id}");
            Console.WriteLine($"  target 440 Hz: {targetDb,7:F1} dBFS");
            Console.WriteLine($"  other 1000 Hz: {otherDb,7:F1} dBFS");
            Console.WriteLine($"  separation:    {targetDb - otherDb,7:F1} dB");
            Console.WriteLine($"  level: tone written at {Amplitude} peak, captured RMS {Rms(samples):F4} (expected {Amplitude / Math.Sqrt(2):F4})");

            var pass = targetDb - otherDb >= 40;
            Console.WriteLine(pass ? "PASS" : "FAIL");
            return pass ? 0 : 1;
        }
        finally
        {
            foreach (var process in new[] { target, other })
            {
                if (!process.HasExited) process.Kill();
                process.Dispose();
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    private const float Amplitude = 0.03f;

    private static string Tone(string folder, string name, double hz)
    {
        var path = Path.Combine(folder, $"{name}.wav");
        using var writer = new WavFileWriter(path, Rate);
        var samples = new float[Rate * 6];
        for (var i = 0; i < samples.Length; i++) samples[i] = Amplitude * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        writer.Write(samples);
        return path;
    }

    private static Process Play(string wav) => Process.Start(new ProcessStartInfo("powershell",
        ["-NoProfile", "-Command", $"(New-Object System.Media.SoundPlayer '{wav}').PlaySync()"])
    {
        CreateNoWindow = true,
        UseShellExecute = false,
    })!;

    // Keeps the first channel only, which is enough for a mono tone.
    private static List<float> Capture(IAudioSource source, TimeSpan duration)
    {
        var samples = new List<float>();
        var channels = 2;
        source.FormatChanged += f => channels = f.Channels;
        source.SamplesAvailable += chunk =>
        {
            var span = chunk.Span;
            lock (samples)
                for (var i = 0; i < span.Length; i += channels) samples.Add(span[i]);
        };
        source.Start();
        Thread.Sleep(duration);
        source.Dispose();
        return samples;
    }

    private static double Goertzel(List<float> samples, double hz)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * hz / Rate);
        double s1 = 0, s2 = 0;
        foreach (var sample in samples)
        {
            var s = sample + coefficient * s1 - s2;
            s2 = s1;
            s1 = s;
        }
        var magnitude = Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - coefficient * s1 * s2)) * 2 / Math.Max(1, samples.Count);
        return 20 * Math.Log10(Math.Max(magnitude, 1e-10));
    }

    private static double Rms(List<float> samples) => samples.Count == 0 ? 0 : Math.Sqrt(samples.Average(s => (double)s * s));
}
