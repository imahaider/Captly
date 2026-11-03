using System.Diagnostics;
using System.Runtime.InteropServices;
using Captly.Audio;
using Captly.Core.Settings;
using Captly.Core.Windowing;
using Captly.Windowing;

namespace Captly.Bench;

/// <summary>Shows how Captly scores an app's windows, then follows the best one as the overlay would.</summary>
internal static class WindowCheck
{
    private static readonly nint PerMonitorAwareV2 = -4;

    public static int Run(string app, string? mediaTitle, int seconds)
    {
        // Like Captly itself, so every coordinate is in physical pixels.
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);

        var exe = app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app;
        var tree = ProcessSnapshot.Take();
        var roots = Process.GetProcessesByName(exe).Select(p => tree.RootOf(p.Id)).ToHashSet();
        if (roots.Count == 0)
        {
            Console.WriteLine($"{app} is not running.");
            return 1;
        }

        var primary = Monitors.Primary();
        var right = Monitors.FromPoint(new PixelPoint(primary.Bounds.Right + 10, primary.Bounds.Top + 10));
        Console.WriteLine($"Primary monitor {primary.Bounds.Width}x{primary.Bounds.Height} @ {primary.Scale * 100:F0}%, work area {primary.WorkArea}");
        if (right != primary) Console.WriteLine($"Monitor to the right {right.Bounds} @ {right.Scale * 100:F0}%");

        var windows = WindowFinder.List(pid => roots.Contains(tree.RootOf(pid)));
        Console.WriteLine($"{windows.Count} top-level windows, media title \"{mediaTitle}\"");
        foreach (var w in windows.Where(w => w.IsVisible))
        {
            var flags = string.Join(",", new[] { w.IsCloaked ? "cloaked" : null, w.IsMinimised ? "min" : null, w.IsToolWindow ? "tool" : null, w.HasOwner ? "owned" : null, w.IsForeground ? "front" : null }.OfType<string>());
            var score = WindowScoring.IsCandidate(w) ? WindowScoring.Score(w, mediaTitle).ToString() : "-";
            Console.WriteLine($"  {score,5}  {w.Bounds.Width}x{w.Bounds.Height} at {w.Bounds.Left},{w.Bounds.Top}  [{flags}]  {WindowFinder.ClassName(w.Handle)}  \"{w.Title}\"");
        }

        var best = WindowScoring.Pick(windows, mediaTitle);
        if (best == null)
        {
            Console.WriteLine("No window to pin to.");
            return 1;
        }

        Console.WriteLine($"Following \"{best.Title}\" for {seconds} s. Move, resize, maximise, go fullscreen and minimise it.");
        var settings = new AppSettings();
        WindowPlacement? last = null;
        var stop = Stopwatch.StartNew();
        while (stop.Elapsed.TotalSeconds < seconds && WindowFinder.Exists(best.Handle))
        {
            var placement = WindowFinder.Placement(best.Handle, Environment.ProcessId);
            if (!placement.SameAs(last))
            {
                var layout = CaptionPlacement.OnWindow(placement, settings.Position, settings.Appearance);
                var state = placement.IsMinimised ? "minimised" : placement.IsHidden ? "hidden" : placement.IsFullscreen ? "fullscreen" : "window";
                Console.WriteLine($"{stop.Elapsed.TotalSeconds,5:F1}s  {state,-10} {placement.Bounds.Width}x{placement.Bounds.Height} at {placement.Bounds.Left},{placement.Bounds.Top}"
                    + $"  monitor {placement.Monitor.Bounds.Width}x{placement.Monitor.Bounds.Height} @ {placement.Monitor.Scale * 100:F0}%"
                    + $"  above {placement.Above.Count}  ->  text {layout.FontSize:F1}, anchor {layout.Anchor.X},{layout.Anchor.Y}, {(layout.Visible ? "shown" : "hidden")}");
                last = placement;
            }
            Thread.Sleep(250);
        }
        return 0;
    }

    [DllImport("user32")] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
