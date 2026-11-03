using Captly.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Captly.Core.Tests;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("captly-logs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WritesFormattedLinesToADatedFile()
    {
        using (var provider = new RollingFileLoggerProvider(_folder, clock: new FakeClock(Start)))
            provider.CreateLogger("Test").LogInformation("Hello {Name}", "world");

        var line = File.ReadAllText(Path.Combine(_folder, "captly-2026-11-03.log"));
        Assert.Contains("2026-11-03 12:00:00.000 INF Test: Hello world", line);
    }

    [Fact]
    public void IncludesExceptions()
    {
        using (var provider = new RollingFileLoggerProvider(_folder, clock: new FakeClock(Start)))
            provider.CreateLogger("Test").LogError(new InvalidOperationException("boom"), "Failed");

        Assert.Contains("boom", File.ReadAllText(Path.Combine(_folder, "captly-2026-11-03.log")));
    }

    [Fact]
    public void SkipsDebugLines()
    {
        using (var provider = new RollingFileLoggerProvider(_folder, clock: new FakeClock(Start)))
            provider.CreateLogger("Test").LogDebug("Noise");

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void StartsANewFileEachDayAndPrunesTheOldest()
    {
        var clock = new FakeClock(Start);
        using (var provider = new RollingFileLoggerProvider(_folder, keepFiles: 2, clock))
        {
            var logger = provider.CreateLogger("Test");
            for (var day = 0; day < 4; day++)
            {
                clock.Now = Start.AddDays(day);
                logger.LogInformation("Day {Day}", day);
            }
        }

        Assert.Equal(
            ["captly-2026-11-05.log", "captly-2026-11-06.log"],
            Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
    }
}
