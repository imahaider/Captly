using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Captly.Audio;
using Captly.Core;
using Captly.Core.Audio;
using Captly.Core.Logging;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.Core.Windowing;
using Captly.Media;
using Captly.Speech;
using Captly.UI;
using Captly.UI.Gallery;
using Captly.UI.Overlay;
using Captly.Updates;
using Captly.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Captly.App;

internal static class Program
{
    private const string DebugCaptureFlag = "--debug-capture";
    private const string GalleryFlag = "--gallery";
    private const string GalleryRenderFlag = "--gallery-render";
    private const string UiRenderFlag = "--ui-render";
    private const string OverlayFlag = "--overlay";
    private const string OverlayDemoFlag = "--overlay-demo";
    private const string TranscriptsFlag = "--transcripts";
    private const string SettingsFlag = "--settings";
    private const string SetupFlag = "--setup";
    private const string InstanceName = @"Local\Captly.SingleInstance";
    private const string OpenSettingsSignal = @"Local\Captly.OpenSettings";
    private const int DefaultDebugSeconds = 20;
    private const int DefaultOverlaySeconds = 120;
    private const int DemoSeconds = 42;
    private const int DemoPauseSteps = 8;
    private static readonly TimeSpan DemoStep = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// With no flags, Captly runs in the tray. A second copy opens Settings in the first and exits.
    /// Developer flags: [--settings [page]] [--setup] [--debug-capture [seconds] [path]] [--gallery]
    /// [--gallery-render folder [scale]] [--ui-render folder [scale]]
    /// [--overlay [seconds] [wav | system | app] [loop] [hidden] [on=app]] [--overlay-demo [app] [seconds]] [--transcripts folder].
    /// --settings opens Settings at start. --setup shows first-run setup even when it is done. --debug-capture records all system audio into a 16 kHz WAV.
    /// --gallery shows every control in both themes. --gallery-render and --ui-render write the
    /// gallery, or the real Settings screens and popover, as PNGs.
    /// --overlay captions the source settings choose, or a WAV without sound, all system audio,
    /// or one app such as chrome, then exits.
    /// --overlay-demo shows scripted captions, with no audio or model, pinned to an app's window if named.
    /// --transcripts writes this run's transcripts to another folder.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser();
        using var services = BuildServices(paths);
        var logger = services.GetRequiredService<ILogger<App>>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");

        logger.LogInformation("Captly {Version} starting", Version);
        var settings = services.GetRequiredService<SettingsStore>().Load();
        logger.LogInformation("Settings loaded, version {Version}", settings.Version);

        var capture = Array.IndexOf(args, DebugCaptureFlag);
        if (capture >= 0) return RunDebugCapture(services, args[(capture + 1)..], logger);

        var app = new App();
        ThemeManager.Install(app, settings.General.Theme);

        // A tray app should outlive one bad event: log it and carry on, rather than vanish.
        app.DispatcherUnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unhandled error on the UI thread");
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task error");
            e.SetObserved();
        };

        var transcripts = Array.IndexOf(args, TranscriptsFlag);
        var transcriptFolder = transcripts >= 0 && args.Length > transcripts + 1 ? args[transcripts + 1] : null;
        if (transcripts >= 0) args = [.. args[..transcripts], .. args[Math.Min(args.Length, transcripts + 2)..]];

        var overlay = Array.IndexOf(args, OverlayFlag);
        if (overlay >= 0) return RunOverlay(app, services, settings, args[(overlay + 1)..], transcriptFolder, logger);
        var demo = Array.IndexOf(args, OverlayDemoFlag);
        if (demo >= 0) return RunOverlayDemo(app, services, settings, args[(demo + 1)..], logger);

        var render = Array.IndexOf(args, GalleryRenderFlag);
        if (render >= 0)
        {
            GalleryRenderer.Render(args[render + 1], args.Length > render + 2 ? double.Parse(args[render + 2]) : 1);
            return 0;
        }
        var uiRender = Array.IndexOf(args, UiRenderFlag);
        if (uiRender >= 0)
        {
            UiRenderer.Render(services, args[uiRender + 1], args.Length > uiRender + 2 ? double.Parse(args[uiRender + 2]) : 1, Version);
            return 0;
        }
        if (args.Contains(GalleryFlag)) return app.Run(new GalleryWindow());

        var settingsAt = Array.IndexOf(args, SettingsFlag);
        var openPage = settingsAt < 0 ? null : args.Length > settingsAt + 1 ? args[settingsAt + 1] : "General";
        return RunTray(app, services, settings, openPage, args.Contains(SetupFlag), InstanceSuffix(paths), logger);
    }

    /// <summary>The version from the build, without the source revision the SDK appends.</summary>
    public static string Version
    {
        get
        {
            var informational = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
    }

    private static ServiceProvider BuildServices(AppPaths paths)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddLogging(logging => logging.AddProvider(new RollingFileLoggerProvider(paths.LogFolder)));
        services.AddSingleton(provider => new SettingsStore(paths.SettingsFile, provider.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton<IAudioSourceFactory, AudioSourceFactory>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IModelManager>(provider => new ModelManager(
            paths.ModelFolder, provider.GetRequiredService<HttpClient>(), provider.GetRequiredService<ILogger<ModelManager>>()));
        return services.BuildServiceProvider();
    }

    // One copy runs. A second launch, such as from the Start menu, opens Settings in it instead.
    private static int RunTray(App app, ServiceProvider services, AppSettings initial, string? openPage, bool forceSetup, string instance, ILogger logger)
    {
        using var mutex = new Mutex(true, InstanceName + instance, out var first);
        using var openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsSignal + instance);
        if (!first)
        {
            logger.LogInformation("Captly is already running; asking it to open Settings");
            openSignal.Set();
            return 0;
        }

        var store = services.GetRequiredService<SettingsStore>();
        using var settings = new SettingsService(initial, store.Save);
        var state = new AppState();
        var loggers = services.GetRequiredService<ILoggerFactory>();
        var models = services.GetRequiredService<IModelManager>();
        using var coordinator = new Coordinator(services.GetRequiredService<IAudioSourceFactory>(), new SmtcMediaWatcher(loggers.CreateLogger<SmtcMediaWatcher>()),
            settings, models, state, loggers, app.Dispatcher);
        using var shell = new TrayShell(app, settings, state, coordinator, models,
            new StartupRegistration(StartupRegistration.CommandFor(Environment.ProcessPath!)),
            new GitHubUpdateChecker(services.GetRequiredService<HttpClient>(), GitHubUpdateChecker.Repository, Version),
            Version, loggers.CreateLogger<TrayShell>());

        var waiter = ThreadPool.RegisterWaitForSingleObject(openSignal, (_, _) => app.Dispatcher.BeginInvoke(() => shell.OpenSettings()), null, Timeout.Infinite, false);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Setup comes first on a fresh install. Captions start when it is done (PLAN.md Phase 9).
        var setup = forceSetup || SetupModel.IsNeeded(settings.Current.SetupCompleted, models);
        app.Startup += (_, _) =>
        {
            coordinator.Start(on: settings.Current.General.AutoCaption && !setup);
            shell.Start();
            if (setup) shell.OpenSetup();
            else if (openPage != null) shell.OpenSettings(openPage);
        };
        var code = app.Run();
        waiter.Unregister(null);
        logger.LogInformation("Captly quitting");
        return code;
    }

    // One copy per data folder: one per user normally, but a test profile (CAPTLY_DATA) runs on its own.
    private static string InstanceSuffix(AppPaths paths)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.RoamingRoot.ToUpperInvariant()));
        return "." + Convert.ToHexString(bytes, 0, 6);
    }

    private static int RunDebugCapture(ServiceProvider services, string[] args, ILogger logger)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : DefaultDebugSeconds;
        var path = args.Length > 1 ? args[1] : services.GetRequiredService<AppPaths>().DebugWavFile;
        try
        {
            using var source = services.GetRequiredService<IAudioSourceFactory>().CreateSystemWide();
            var result = DebugAudioDump.RunAsync(source, path, TimeSpan.FromSeconds(seconds)).GetAwaiter().GetResult();
            logger.LogInformation("Debug capture wrote {Seconds:F1}s to {Path}, peak {Peak:F3}, dropped {Dropped}, from {Rate} Hz {Channels} channels",
                result.SecondsWritten, path, result.Peak, result.DroppedSamples, result.SourceFormat.SampleRate, result.SourceFormat.Channels);
            return 0;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Debug capture failed");
            return 1;
        }
    }

    // Captions live audio, or a WAV, for a set time, without the tray.
    private static int RunOverlay(App app, ServiceProvider services, AppSettings settings, string[] args, string? transcriptFolder, ILogger logger)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : DefaultOverlaySeconds;
        var models = services.GetRequiredService<IModelManager>();
        if (models.Available.FirstOrDefault(m => m.Id == settings.Engine.Model) is not { IsInstalled: true })
        {
            logger.LogError("Model {Model} is not installed. Run Bench download first", settings.Engine.Model);
            return 1;
        }

        // The second argument picks the source for this run only: a WAV, "system", or an app such as "chrome".
        // "loop" replays a WAV, and "hidden" keeps the captions off screen, for long runs.
        // "on=app" shows a WAV's captions on that app's window, for screenshots.
        var loop = args.Contains("loop");
        var hidden = args.Contains("hidden");
        var pinTo = args.FirstOrDefault(a => a.StartsWith("on=", StringComparison.Ordinal))?[3..];
        args = args.Where(a => a is not ("loop" or "hidden") && !a.StartsWith("on=", StringComparison.Ordinal)).ToArray();
        var choice = args.Length > 1 ? args[1] : null;
        var wav = choice != null && choice.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? choice : null;
        if (choice != null && wav == null)
        {
            settings = settings with
            {
                Source = choice.Equals("system", StringComparison.OrdinalIgnoreCase)
                    ? settings.Source with { Mode = SourceMode.System }
                    : settings.Source with { Mode = SourceMode.Pinned, PinnedExe = choice },
            };
        }

        var loggers = services.GetRequiredService<ILoggerFactory>();
        using var service = new SettingsService(settings, SaveFixedSpot(services));
        using var coordinator = new Coordinator(services.GetRequiredService<IAudioSourceFactory>(), new SmtcMediaWatcher(loggers.CreateLogger<SmtcMediaWatcher>()),
            service, models, new AppState(), loggers, app.Dispatcher, transcriptFolder, wav, loop, pinTo == null ? null : FindAppRoot(pinTo));

        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        stop.Tick += (_, _) => app.Shutdown();
        app.Startup += (_, _) =>
        {
            coordinator.Start(on: true);
            coordinator.SetHidden(hidden);
            stop.Start();
        };
        return app.Run();
    }

    // Saves only where the captions were dragged, so a run's own source choice is never stored.
    private static Action<AppSettings> SaveFixedSpot(ServiceProvider services) => settings =>
    {
        var store = services.GetRequiredService<SettingsStore>();
        var saved = store.Load();
        store.Save(saved with { Position = saved.Position with { Fixed = settings.Position.Fixed } });
    };

    // Scripted captions, for checking the look, click-through and pinning with no audio or model.
    // With an app name, such as notepad, they are pinned to that app's window.
    private static int RunOverlayDemo(App app, ServiceProvider services, AppSettings settings, string[] args, ILogger logger)
    {
        string[] script =
        [
            "The fan", "The fan pulls in", "The fan pulls in huge amounts of air,",
            "The fan pulls in huge amounts of air, and most of it", "The fan pulls in huge amounts of air, and most of it flows around the core.",
        ];

        var seconds = args.FirstOrDefault(a => int.TryParse(a, out _)) is { } text ? int.Parse(text) : DemoSeconds;
        var appName = args.FirstOrDefault(a => !int.TryParse(a, out _));
        var overlay = new OverlayWindow();
        var view = new CaptionView(settings, overlay);
        var save = SaveFixedSpot(services);
        CaptionPinner? pinner = null;
        var step = 0;
        var timer = new DispatcherTimer { Interval = DemoStep };
        timer.Tick += (_, _) =>
        {
            var position = step++ % (script.Length + DemoPauseSteps);
            if (position < script.Length) view.Show(new CaptionEvent(script[position], position == script.Length - 1, TimeSpan.Zero));
            if (step * DemoStep.TotalSeconds > seconds) app.Shutdown();
        };

        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += (_, _) =>
        {
            var pid = appName == null ? null : FindAppRoot(appName);
            if (appName != null && pid == null) logger.LogWarning("No window found for {App}", appName);
            var windows = pid == null ? null : new WindowTracker(ProcessSnapshot.Take, services.GetRequiredService<ILogger<WindowTracker>>());
            pinner = new CaptionPinner(overlay, windows, settings, spot =>
            {
                settings = settings with { Position = settings.Position with { Fixed = new FixedPosition(spot.X, spot.Y) } };
                save(settings);
                pinner!.Apply(settings);
            });
            pinner.Follow(pid, null);
            timer.Start();
        };
        var code = app.Run();
        pinner?.Dispose();
        return code;
    }

    // The tracker looks for windows again every 2 s, so the app's window need not be open yet.
    private static int? FindAppRoot(string name)
    {
        var exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var processes = Process.GetProcessesByName(exe);
        var pid = processes.FirstOrDefault()?.Id;
        foreach (var process in processes) process.Dispose();
        return pid is { } found ? ProcessSnapshot.Take().RootOf(found) : null;
    }
}
