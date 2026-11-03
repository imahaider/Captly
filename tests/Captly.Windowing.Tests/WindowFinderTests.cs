using System.Text.RegularExpressions;
using Captly.Core.Audio;
using Captly.Core.Windowing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Windowing.Tests;

public class WindowFinderTests
{
    private static readonly int Self = Environment.ProcessId;

    [Fact]
    public void List_FindsTheWindow_WithItsTitleAndBounds()
    {
        using var window = new TestWindow(100, 120, 640, 360);

        var found = WindowFinder.List(pid => pid == Self).Single(w => w.Handle == window.Handle);

        Assert.Equal(window.Title, found.Title);
        Assert.Equal(PixelRect.FromSize(100, 120, 640, 360), found.Bounds);
        Assert.Equal(Self, found.ProcessId);
        Assert.True(WindowScoring.IsCandidate(found));
    }

    [Fact]
    public void List_LeavesOutOtherProcesses()
    {
        using var window = new TestWindow(100, 120, 640, 360);

        Assert.DoesNotContain(WindowFinder.List(pid => pid != Self), w => w.Handle == window.Handle);
    }

    [Fact]
    public void Placement_ReportsMinimisedAndRestored()
    {
        using var window = new TestWindow(100, 120, 640, 360);

        window.Minimise();
        Assert.True(WindowFinder.Placement(window.Handle, 0).IsMinimised);

        window.Restore();
        var placement = WindowFinder.Placement(window.Handle, 0);
        Assert.False(placement.IsMinimised);
        Assert.False(placement.IsHidden);
    }

    [Fact]
    public void Placement_ListsWindowsAbove_ExceptTheExcludedProcess()
    {
        using var video = new TestWindow(100, 120, 640, 360);
        using var cover = new TestWindow(200, 300, 300, 200);
        var coverBounds = PixelRect.FromSize(200, 300, 300, 200);

        Assert.Contains(coverBounds, WindowFinder.Placement(video.Handle, 0).Above);
        Assert.DoesNotContain(coverBounds, WindowFinder.Placement(video.Handle, Self).Above);
    }

    [Fact]
    public void Placement_CoveringTheMonitor_IsFullscreen()
    {
        var monitor = Monitors.Primary().Bounds;
        using var window = new TestWindow(monitor.Left, monitor.Top, monitor.Width, monitor.Height);

        var placement = WindowFinder.Placement(window.Handle, 0);

        Assert.Equal(monitor, placement.Monitor.Bounds);
        Assert.True(placement.IsFullscreen);
    }

    [Fact]
    public void Monitors_PrimaryIsAtTheOrigin_WithItsScale()
    {
        var primary = Monitors.Primary();

        Assert.True(primary.Bounds.Contains(new PixelPoint(0, 0)));
        Assert.True(primary.Bounds.Contains(primary.WorkArea));
        Assert.InRange(primary.Scale, 1, 5);
        Assert.Equal(primary, Monitors.FromPoint(new PixelPoint(1, 1)));
    }

    [Fact]
    public void Tracker_FollowsTheWindow_AndStops()
    {
        using var window = new TestWindow(100, 120, 640, 360);
        using var tracker = new WindowTracker(() => new ProcessTree([]), NullLogger.Instance, 0, new SynchronizationContext());
        var changes = new List<WindowPlacement?>();
        tracker.PlacementChanged += changes.Add;

        tracker.Track(Self, window.Title);
        Assert.Equal(window.Handle, tracker.Current!.Handle);
        Assert.Equal(PixelRect.FromSize(100, 120, 640, 360), tracker.Current.Bounds);

        window.Move(300, 200, 800, 450);
        tracker.Tick();
        Assert.Equal(PixelRect.FromSize(300, 200, 800, 450), tracker.Current!.Bounds);

        var count = changes.Count;
        tracker.Tick();
        Assert.Equal(count, changes.Count);

        tracker.Track(null, null);
        Assert.Null(tracker.Current);
        Assert.Null(changes[^1]);
    }

    // Listeners such as the overlay may be gone by then.
    [Fact]
    public void Tracker_RaisesNothing_WhenDisposed()
    {
        using var window = new TestWindow(100, 120, 640, 360);
        var tracker = new WindowTracker(() => new ProcessTree([]), NullLogger.Instance, 0, new SynchronizationContext());
        tracker.Track(Self, window.Title);
        var raised = false;
        tracker.PlacementChanged += _ => raised = true;

        tracker.Dispose();
        tracker.Track(Self, "something else");

        Assert.False(raised);
    }

    [Fact]
    public void Tracker_PrefersTheWindowNamedAfterTheMedia()
    {
        using var other = new TestWindow(100, 120, 1200, 700);
        using var video = new TestWindow(150, 150, 640, 360, $"Inside a jet engine {Guid.NewGuid():N} - YouTube");
        using var tracker = new WindowTracker(() => new ProcessTree([]), NullLogger.Instance, 0, new SynchronizationContext());

        tracker.Track(Self, video.Title.Replace(" - YouTube", ""));

        Assert.Equal(video.Handle, tracker.Current!.Handle);
    }

    [Fact]
    public void Tracker_FindsAnotherWindow_WhenItsWindowCloses()
    {
        var title = $"Captly test {Guid.NewGuid():N}";
        using var second = new TestWindow(100, 120, 300, 200, title + " second");
        var first = new TestWindow(100, 120, 640, 360, title + " first");
        using var tracker = new WindowTracker(() => new ProcessTree([]), NullLogger.Instance, 0, new SynchronizationContext());
        tracker.Track(Self, title + " first");
        Assert.Equal(first.Handle, tracker.Current!.Handle);

        first.Dispose();
        tracker.Tick();

        Assert.NotEqual(first.Handle, tracker.Current?.Handle);
    }

    [Fact]
    public void GraphicsAdapters_HaveVendorAndDeviceIds()
    {
        var adapters = GraphicsAdapters.List();

        Assert.All(adapters, a =>
        {
            // Usually PCI numbers, but Qualcomm reports 4D4F4351, "QCOM".
            Assert.Matches(new Regex("^[0-9A-F]{4,8}:[0-9A-F]{4,8}$"), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
        });
        Assert.Equal(adapters.Count, adapters.Select(a => a.Id).Distinct().Count());
    }
}
