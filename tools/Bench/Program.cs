using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Captly.Audio;
using Captly.Bench;
using Captly.Core;
using Captly.Core.Speech;
using Captly.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Usage:
//   Bench run <model> <wav> [reference.txt] [--update-ms 500]   as fast as it will go: RTF, WER
//   Bench live <model> <wav> [--update-ms 500]                  at real-time pace: caption latency
//   Bench captions <model> <seconds>                            prints captions for system audio
//   Bench download <model>                                      fetches and verifies a model
//   Bench catalog <out.json>                                    rebuilds models.json from local files
//   Bench apps [seconds]                                        lists apps playing sound, and Auto's pick
//   Bench isolate                                               two-tone check of per-app capture
//   Bench media [seconds]                                       media sessions and the app each maps to
//   Bench mediaplay <wav>                                       scripted play, pause, seek and new title
//   Bench windows <app> [title] [seconds]                       window scores, then the placement live
//   Bench speedtest                                             in-app speed test and recommendation
//   Bench player                                                a scripted window for Captly --overlay-demo bench
//   Bench stage <bmp> <title> [seconds]                         a picture in a titled window, for caption screenshots
//   Bench pipeline <wav> <seconds> [update-ms]                the app's audio path without its window: CPU and audio given
if (args.Length > 0 && args[0] == "apps") return SourceChecks.Apps(args.Length > 1 ? int.Parse(args[1]) : 10);
if (args.Length > 0 && args[0] == "isolate") return SourceChecks.Isolate();
if (args.Length > 0 && args[0] == "media") return MediaCheck.Run(args.Length > 1 ? int.Parse(args[1]) : 10);
if (args.Length > 1 && args[0] == "mediaplay") return await MediaCheck.Play(args[1]);
if (args.Length > 1 && args[0] == "windows") return WindowCheck.Run(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? int.Parse(args[3]) : 20);
if (args.Length > 0 && args[0] == "speedtest") return SpeedCheck.Run();
if (args.Length > 0 && args[0] == "player") return PlayerCheck.Run();
if (args.Length > 2 && args[0] == "stage") return StageCheck.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 40);
if (args.Length > 2 && args[0] == "pipeline") return PipelineCheck.Run(args[1], int.Parse(args[2]), args.Length > 3 ? int.Parse(args[3]) : 500, args.Contains("pool"), args.Contains("reset"));

if (args.Length < 2)
{
    Console.WriteLine("Bench run|live|captions|download|catalog, see Program.cs");
    return 2;
}

var updateMs = 500;
var flag = Array.IndexOf(args, "--update-ms");
if (flag >= 0)
{
    updateMs = int.Parse(args[flag + 1]);
    args = [.. args[..flag], .. args[(flag + 2)..]];
}

var paths = AppPaths.ForCurrentUser();
if (args[0] == "catalog") return CatalogBuilder.Run(args[1], paths.ModelFolder);

using var http = new HttpClient();
var manager = new ModelManager(paths.ModelFolder, http, NullLogger<ModelManager>.Instance);
var model = manager.Available.FirstOrDefault(m => m.Id == $"moonshine-{args[1]}-streaming");
if (model == null)
{
    Console.WriteLine($"Unknown model {args[1]}. Use tiny, small or medium.");
    return 2;
}

Console.WriteLine($"{model.DisplayName}, process {RuntimeInformation.ProcessArchitecture}, update {updateMs} ms");
switch (args[0])
{
    case "download":
        var lastShown = -1;
        await manager.DownloadAsync(model.Id, new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (percent / 10 == lastShown) return;
            lastShown = percent / 10;
            Console.WriteLine($"{percent}%");
        }), CancellationToken.None);
        Console.WriteLine("Installed");
        return 0;

    case "run":
        return RunFast(LoadEngine(), args[2], args.Length > 3 ? args[3] : null);

    case "live":
        return RunLive(LoadEngine(), args[2], args.Length > 3 ? double.Parse(args[3]) : null);

    case "captions":
        return RunCaptions(LoadEngine(), int.Parse(args[2]));

    default:
        Console.WriteLine($"Unknown command {args[0]}");
        return 2;
}

MoonshineEngine LoadEngine()
{
    var engine = new MoonshineEngine();
    var load = Stopwatch.StartNew();
    engine.Load(new EngineOptions(manager.GetModelPath(model.Id), model.Architecture)
    {
        UpdateInterval = TimeSpan.FromMilliseconds(updateMs),
    });
    Console.WriteLine($"Load {load.ElapsedMilliseconds} ms");
    return engine;
}

int RunFast(MoonshineEngine engine, string wav, string? referencePath)
{
    var audio = Wav.ReadMono16k(wav);
    var seconds = audio.Length / 16000.0;
    var finals = new List<string>();
    engine.Caption += c => { if (c.IsFinal && c.Text.Length > 0) finals.Add(c.Text); };

    var pushes = new List<double>();
    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
    var wall = Stopwatch.StartNew();
    foreach (var chunk in Wav.Chunks(audio, 1600))
    {
        var push = Stopwatch.StartNew();
        engine.Push(chunk.Span);
        pushes.Add(push.Elapsed.TotalMilliseconds);
    }
    foreach (var chunk in Wav.Chunks(new float[32000], 1600)) engine.Push(chunk.Span);
    wall.Stop();

    var cpuUsed = Process.GetCurrentProcess().TotalProcessorTime - cpu;
    var updates = pushes.Where(p => p > 1).Order().ToList();
    Console.WriteLine($"Audio {seconds:F1}s");
    Console.WriteLine($"Wall {wall.Elapsed.TotalSeconds:F2}s  RTF {wall.Elapsed.TotalSeconds / seconds:F3}  CPU {cpuUsed.TotalSeconds / seconds * 100:F0}% of one core");
    if (updates.Count > 0)
        Console.WriteLine($"Update ms: mean {updates.Average():F0}  p50 {updates[updates.Count / 2]:F0}  p95 {updates[(int)(updates.Count * 0.95)]:F0}  max {updates[^1]:F0}  n={updates.Count}");
    Console.WriteLine($"Memory peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MB");

    var text = string.Join(" ", finals);
    Console.WriteLine("Text: " + text);
    if (referencePath != null) Console.WriteLine($"WER {Wer.Compute(File.ReadAllText(referencePath), text) * 100:F1}%");
    return 0;
}

// Feeds at wall-clock pace, ending as a real stream does: with the worker's silence padding. An
// optional cut stops the audio mid-sentence, to show that padding is enough to end the line.
int RunLive(MoonshineEngine engine, string wav, double? cutSeconds)
{
    var audio = Wav.ReadMono16k(wav);
    if (cutSeconds is { } cut) audio = audio[..(int)(cut * 16000)];
    var padding = new float[(int)(CaptionWorker.MaxPadding.TotalSeconds * 16000)];

    var clock = Stopwatch.StartNew();
    var firstText = new List<double>();
    var seen = new HashSet<TimeSpan>();
    var pushed = TimeSpan.Zero;
    var partials = 0;
    var lastPartialOpen = false;

    engine.Caption += c =>
    {
        if (seen.Add(c.StreamTime) && c.Text.Length > 0) firstText.Add((pushed - c.StreamTime).TotalMilliseconds);
        lastPartialOpen = !c.IsFinal;
        if (!c.IsFinal) partials++;
        Console.WriteLine($"{pushed.TotalSeconds,6:F1}s {(c.IsFinal ? "FINAL  " : "partial")} {c.Text}");
    };

    foreach (var chunk in Wav.Chunks(audio, 1600).Concat(Wav.Chunks(padding, 1600)))
    {
        var due = pushed;
        var wait = due - clock.Elapsed;
        if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        pushed += TimeSpan.FromSeconds(chunk.Length / 16000.0);
        engine.Push(chunk.Span);
    }

    firstText.Sort();
    Console.WriteLine($"Lines {seen.Count}, partials {partials}, audio ends at {audio.Length / 16000.0:F1}s, last line {(lastPartialOpen ? "LEFT OPEN" : "finished")}");
    if (firstText.Count > 0)
        Console.WriteLine($"Line start to first words, ms: mean {firstText.Average():F0}  p50 {firstText[firstText.Count / 2]:F0}  max {firstText[^1]:F0}");
    return 0;
}

// The temporary console caption output: system audio, through the real pipeline.
int RunCaptions(MoonshineEngine engine, int seconds)
{
    using var loggers = LoggerFactory.Create(_ => { });
    using var source = new AudioSourceFactory(loggers).CreateSystemWide();
    var preprocessor = new AudioPreprocessor();
    preprocessor.Attach(source);

    // A redirected console has no width, so it gets finished lines only.
    var live = !Console.IsOutputRedirected;
    var partial = false;
    engine.Caption += c =>
    {
        if (c.IsFinal)
        {
            if (live) Console.Write("\r" + new string(' ', Console.BufferWidth - 1) + "\r");
            if (c.Text.Length > 0) Console.WriteLine(c.Text);
            partial = false;
            return;
        }
        if (!live) return;
        var shown = c.Text.Length > Console.BufferWidth - 2 ? "..." + c.Text[^(Console.BufferWidth - 6)..] : c.Text;
        Console.Write("\r" + shown.PadRight(Console.BufferWidth - 1));
        partial = true;
    };

    using var worker = new CaptionWorker(preprocessor, engine, loggers.CreateLogger<CaptionWorker>());
    worker.Start();
    source.Start();
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    source.Stop();
    Thread.Sleep(CaptionWorker.MaxPadding + TimeSpan.FromSeconds(1));
    if (partial) Console.WriteLine();
    Console.WriteLine($"Dropped {preprocessor.DroppedSamples} samples");
    return 0;
}
