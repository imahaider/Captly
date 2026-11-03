using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Captly.Core.Logging;

/// <summary>Writes one log file per day on a background thread and keeps the newest few.</summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const string Prefix = "captly-";

    private readonly string _folder;
    private readonly int _keepFiles;
    private readonly TimeProvider _clock;
    private readonly BlockingCollection<string> _lines = new(boundedCapacity: 10_000);
    private readonly Thread _writer;

    public RollingFileLoggerProvider(string folder, int keepFiles = 7, TimeProvider? clock = null)
    {
        _folder = folder;
        _keepFiles = keepFiles;
        _clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(folder);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Captly log writer" };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Stops the writer once everything queued is on disk.</summary>
    public void Dispose()
    {
        _lines.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(5));
        _lines.Dispose();
    }

    private void Enqueue(string line)
    {
        // Logging must never hold up the caller, so a full queue drops the line.
        if (!_lines.IsAddingCompleted) _lines.TryAdd(line);
    }

    private void WriteLoop()
    {
        foreach (var line in _lines.GetConsumingEnumerable())
        {
            var date = line[..10];
            var path = Path.Combine(_folder, $"{Prefix}{date}.log");
            var isNewFile = !File.Exists(path);
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
                if (isNewFile) Prune();
            }
            catch (IOException)
            {
                // A log line is never worth stopping the app for.
            }
        }
    }

    private void Prune()
    {
        var old = Directory.GetFiles(_folder, $"{Prefix}*.log").OrderDescending().Skip(_keepFiles);
        foreach (var file in old) File.Delete(file);
    }

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var time = provider._clock.GetLocalNow();
            var line = $"{time:yyyy-MM-dd HH:mm:ss.fff} {Level(logLevel)} {category}: {formatter(state, exception)}";
            if (exception != null) line += Environment.NewLine + exception;
            provider.Enqueue(line);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "DBG",
        };
    }
}
