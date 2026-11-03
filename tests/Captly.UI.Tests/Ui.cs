using System.Windows;
using System.Windows.Threading;
using Captly.Core.Settings;

namespace Captly.UI.Tests;

/// <summary>WPF needs one STA thread with a running dispatcher, so every test runs on the same one.</summary>
internal static class Ui
{
    private static readonly Lazy<Dispatcher> Thread = new(Start);

    public static void Run(Action action) => Thread.Value.Invoke(action);

    public static T Run<T>(Func<T> action) => Thread.Value.Invoke(action);

    /// <summary>Lets queued layout and bindings finish.</summary>
    public static void Settle() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, () => { });

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeManager.Install(app, ThemeChoice.Light);
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
